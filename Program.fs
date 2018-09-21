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

type BotState = { TotalSendedPics: int; TotalUsersToday: int }
type StatsMessage = 
    | UpdateTotalSendedPics of int
    | UpdateTotalUsers of int
    | StateCommand of UpdateContext

let execute context method =
    method
    |> api context.Config 
    |> Async.Ignore 
    |> Async.Start

let cast f = upcast f : IRequestBase<'a>

let sendState context state =
    maybe {
        let! message = context.Update.Message
        sprintf "%i 🐱 sent\n%i 👥 today" state.TotalSendedPics state.TotalUsersToday
        |> sendMessage message.Chat.Id
        |> execute context
    } |> ignore
    state
    
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

let statsAgent = MailboxProcessor.Start(fun inbox ->
    let rec messageLoop state = async {
        let! message = inbox.Receive()
        
        return! messageLoop <| 
        match message with
        | StatsMessage.StateCommand context ->
            sendState context state
        | StatsMessage.UpdateTotalSendedPics totalCats ->
            { state with TotalSendedPics = totalCats }
        | StatsMessage.UpdateTotalUsers totalUsers -> 
            { state with TotalUsersToday = totalUsers }
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

                StatsMessage.UpdateTotalUsers totalUsers |> statsAgent.Post
                
                return state
            }
            |> Option.defaultValue state 
            |> messageLoop

    }
    Map.empty |> messageLoop)

let meowAgent = MailboxProcessor.Start(fun inbox -> 
    let rec messageLoop state = async {
        let! context = inbox.Receive()
        sendCat context
        let state = state + 1
        StatsMessage.UpdateTotalSendedPics state |> statsAgent.Post
        return! messageLoop state
    }
    messageLoop 0)

let onStats = StatsMessage.StateCommand >> statsAgent.Post

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