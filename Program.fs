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

type CatsCacheState = { 
    Chats: Map<int64,Map<string, DateTime>>
    Cats: Set<string> }

type CacheMessage =
    | SendToChat of int64 * string
    | TrySendToChatFromCache of int64  

type BotState = { 
    TotalPicsSent: int
    TotalUsersToday: int
    TotalCached: int }
 
type StatsMessage = 
    | UpdateTotalPicsSent of int
    | UpdateTotalUsers of int
    | UpdateTotalCached of int
    | StateCommand of UpdateContext

let mutable config = defaultConfig 

let execute method =
    method
    |> api config 
    |> Async.Ignore 
    |> Async.Start

let cast f = upcast f : IRequestBase<'a>

let startAndForget task =
    task 
    |> Async.Catch
    |> Async.Ignore
    |> Async.Start

let random = Random()
let rand min max = random.Next(min, max) 

let sendState context state =
    maybe {
        let! message = context.Update.Message
        sprintf "%i 🐱 sent\n%i 👥 today\n%i 😼 cached" 
            state.TotalPicsSent
            state.TotalUsersToday
            state.TotalCached
        |> sendMessage message.Chat.Id
        |> execute
    } |> ignore
    state
    
let sendCat chatId cat =
    async {
        let file = Uri cat |> FileToSend.Url
        let sendCat id file =
            if cat.EndsWith ".gif" then
                sendDocument id file "" |> cast
            else
                sendPhoto id file "" |> cast
        sendCat chatId file |> execute 
    } |> startAndForget

let tryGetCatForChat state chatId =
    let chatCats =
        state.Chats
        |> Map.tryFind chatId
        |> Option.defaultValue Map.empty
    
    let notContains key =
        chatCats 
        |> Map.containsKey key
        |> not
    
    state.Cats
        |> Set.filter notContains
        |> Seq.sortBy (fun _ -> rand 0 1000)
        |> Seq.tryHead

let addCatAsUsedToChat state chatId cat =
    let chats = state.Chats |> Map.tryAdd chatId Map.empty
    let chatCats =
        chats
        |> Map.find chatId
        |> Map.add cat DateTime.Now

    { Chats = chats |> Map.add chatId chatCats
      Cats = state.Cats |> Set.add cat }

let statsAgent = MailboxProcessor.Start(fun inbox ->
    let rec messageLoop state = async {
        let! message = inbox.Receive()
        
        return! messageLoop <| 
        match message with
        | StatsMessage.StateCommand context ->
            sendState context state
        | StatsMessage.UpdateTotalPicsSent totalCats ->
            { state with TotalPicsSent = totalCats }
        | StatsMessage.UpdateTotalUsers totalUsers ->
            { state with TotalUsersToday = totalUsers }
        | StatsMessage.UpdateTotalCached totalCached ->
            { state with TotalCached = totalCached }
    }
    
    messageLoop {
        TotalPicsSent = 0
        TotalUsersToday = 0
        TotalCached = 0
    })
    
let catsAgent = MailboxProcessor.Start(fun inbox ->
    
    let getCatAndPostToChat chatId =
        async {
            sendChatAction chatId ChatAction.UploadPhoto
            |> execute
            let! json = ApiUrl |> CatsApi.AsyncLoad
                
            CacheMessage.SendToChat (chatId, json.File)
            |> inbox.Post
        } |> startAndForget 

    let rec messageLoop state = async {
        let! message = inbox.Receive()
        let state =
            match message with
            | CacheMessage.SendToChat (chatId, cat) ->
                sendCat chatId cat
                let state = addCatAsUsedToChat state chatId cat
                StatsMessage.UpdateTotalCached state.Cats.Count
                |> statsAgent.Post
                state
            | CacheMessage.TrySendToChatFromCache chatId ->
                let result = tryGetCatForChat state chatId
                match result with
                | Some cat -> CacheMessage.SendToChat (chatId, cat) |> inbox.Post
                | None -> getCatAndPostToChat chatId
                state
        return! messageLoop state
    }
    messageLoop { Chats = Map.empty; Cats = Set.empty })
    
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
        maybe {
            let! message = context.Update.Message
            CacheMessage.TrySendToChatFromCache message.Chat.Id 
            |> catsAgent.Post
        } |> ignore
        let state = state + 1
        StatsMessage.UpdateTotalPicsSent state |> statsAgent.Post
        return! messageLoop state
    }
    messageLoop 0)

let onStats = StatsMessage.StateCommand >> statsAgent.Post

let onStart context =
    maybe {
        let! message = context.Update.Message
        let text = "😸 Hi! I am @MeowCatsBot.\nSend /meow to get random cat or /stats to get bot stats!"
        sendMessage message.Chat.Id text |> execute
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
        config <- { defaultConfig with Token = argv.[0] }
        startBot config update None
        |> Async.RunSynchronously
    0