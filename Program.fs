module Program

open System
open System.Diagnostics
open Funogram.Api
open Funogram.Bot
open Funogram.Types
open Funogram.RequestsTypes
open FSharp.Data
open ExtCore.Control

[<Literal>]
let ApiUrl = "http://aws.random.cat/meow"
type CatsApi = JsonProvider<ApiUrl>

let execute context method =
    method |> api context.Config 
    |> Async.Ignore 
    |> Async.Start

let cast f = upcast f : IRequestBase<'a>

type BotState = { TotalSendedPics: int; TotalUsersToday: int }
type StatsMessageType = UpdateTotalSendedPics | UpdateTotalUsers | StateCommand
type StatsMessage = {
    Type: StatsMessageType
    Data: obj }

let sendState context state =
    maybe {
        let! message = context.Update.Message
        sprintf "%i 🐱 sent\n%i 👥 today" state.TotalSendedPics state.TotalUsersToday
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
        | StatsMessageType.UpdateTotalUsers ->
            match message.Data with
            | :? int as totalUsers ->  { state with TotalUsersToday = totalUsers }
            | _ -> state
    }
    messageLoop { TotalSendedPics = 0; TotalUsersToday = 0 })
    
let usersAgent = MailboxProcessor.Start(fun inbox -> 
    let rec messageLoop state = async {
        let! message = inbox.Receive()
        return!
            maybe {
                let! message = message.Update.Message
                let chatId = message.Chat.Id
                
                let state = state |> Map.add chatId DateTime.Now
                let totalUsers = 
                    state
                    |> Map.filter (fun _ v -> v.Date = DateTime.Today) 
                    |> Map.count
                statsAgent.Post { 
                    Type = StatsMessageType.UpdateTotalUsers
                    Data = totalUsers }
                
                return state
            }
            |> Option.defaultValue state 
            |> messageLoop

    }
    Map.empty |> messageLoop)

let sendCat context =
    maybe {
        let! message = context.Update.Message
        sendChatAction message.Chat.Id ChatAction.UploadPhoto
        |> execute context
        async {
            let! json = ApiUrl |> CatsApi.AsyncLoad
            let file = Uri json.File |> FileToSend.Url        
                
            let sendCat id file =
                if json.File.EndsWith ".gif" then
                    sendDocument id file "" |> cast
                else
                    sendPhoto id file "" |> cast

            sendCat message.Chat.Id file |> execute context 
        } |> Async.Catch |> Async.Ignore |> Async.Start
    } |> ignore

let meowAgent = MailboxProcessor.Start(fun inbox -> 
    let rec messageLoop state = async {
        let! context = inbox.Receive()
        sendCat context
        let newState = state + 1
        statsAgent.Post { 
            Type = StatsMessageType.UpdateTotalSendedPics
            Data = newState }
        return! messageLoop newState
    }
    messageLoop 0)

let onStats context =
    statsAgent.Post { 
        Type = StatsMessageType.StateCommand; 
        Data = context }

let onStart context =
    maybe {
        let! message = context.Update.Message
        let text = "😸 Hi! I am @MeowCatsBot.\nSend /meow to get random cat or /stats to get bot stats!"
        sendMessage message.Chat.Id text |> execute context
    } |> ignore

let update context = 
    usersAgent.Post context
    processCommands context [
        cmd "/start" onStart
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