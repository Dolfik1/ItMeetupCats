module Program

open System
open System.Diagnostics
open Funogram.Api
open Funogram.Bot
open Funogram.Types
open FSharp.Data
open ExtCore.Control
open Funogram.RequestsTypes

[<Literal>]
let ApiUrl = "http://aws.random.cat/meow"
type CatsApi = JsonProvider<ApiUrl>

let execute context method =
    method |> api context.Config 
    |> Async.Ignore 
    |> Async.Start

let cast f = upcast f : IRequestBase<'a>

type BotState = { TotalSendedPics: int }
type StatsMessageType = UpdateTotalSendedPics | StateCommand
type StatsMessage = {
    Type: StatsMessageType
    Data: obj }


let sendState context state =
    maybe {
        let! message = context.Update.Message
        sprintf "%i 🐱 sended" state.TotalSendedPics
        |> sendMessage message.Chat.Id
        |> execute context
    } |> ignore
    state

let statsAgent = MailboxProcessor.Start(fun inbox ->
    let rec messageLoop state = async {
        let! message = inbox.Receive()
        
        return! messageLoop <| 
        match message.Type with
        | StatsMessageType.StateCommand ->
            match message.Data with
            | :? UpdateContext as context -> sendState context state
            | _ -> state
        | StatsMessageType.UpdateTotalSendedPics ->
            match message.Data with
            | :? int as totalCats ->  { state with TotalSendedPics = totalCats }
            | _ -> state

    }
    messageLoop { TotalSendedPics = 0 }
    )

let meowAgent = MailboxProcessor.Start(fun inbox -> 
    let rec messageLoop state = async {
        let! context = inbox.Receive()
        sendChatAction context.Update.Message.Value.Chat.Id ChatAction.UploadPhoto 
        |> execute context
        
        let! json = ApiUrl |> CatsApi.AsyncLoad
        maybe {
            let! message = context.Update.Message
            let file = Uri json.File |> FileToSend.Url        
        
            let sendCat id file =
                if json.File.EndsWith ".gif" then
                    sendDocument id file "" |> cast
                else
                    sendPhoto id file "" |> cast

            sendCat message.Chat.Id file |> execute context 
        } |> ignore
        let newState = state + 1
        statsAgent.Post { 
            Type = StatsMessageType.UpdateTotalSendedPics;
            Data = newState }
        return! messageLoop newState
    }
    messageLoop 0
    )

let onStats context =
    statsAgent.Post { 
        Type = StatsMessageType.StateCommand; 
        Data = context }

let update context = 
    processCommands context [
        cmd "/meow" meowAgent.Post
        cmd "/stats" onStats
    ] |> ignore

[<EntryPoint>]
let main argv =
    match argv.Length with
    | 0 -> printf "Please specify bot token as an argument."
    | _ ->
        startBot {
            defaultConfig with Token = argv.[0]
        } update None |> Async.RunSynchronously
    0