module Program

open System
open Funogram.Api
open Funogram.Bot
open Funogram.Types
open FSharp.Data
open ExtCore.Control
open Funogram.RequestsTypes
open System.Net.Http
open System.IO
open System.Collections.Concurrent
open System.Threading

[<Literal>]
let ApiUrl = "http://aws.random.cat/meow"
type CatsApi = JsonProvider<ApiUrl>

let execute context method =
    method |> api context.Config 
    |> Async.Ignore 
    |> Async.Start

let cast f = upcast f : IRequestBase<'a>

let getCat = 
    let cacheSize = 30
    let currentSize = ref 0
    let httpClient = new HttpClient()
    let cache = ConcurrentBag<Stream>()
    fun fileUri ->
        async {
            let size = Interlocked.Increment(currentSize)
            if size <= cacheSize then
                let file = Uri fileUri
                let! result = httpClient.GetStreamAsync(file) |> Async.AwaitTask
                cache.Add(result)
                return result
            else
                let (_, result) = cache.TryPeek()
                return result
        }

let onMeow context =
    async {
        sendChatAction context.Update.Message.Value.Chat.Id ChatAction.UploadPhoto 
        |> execute context
        
        let! json = ApiUrl |> CatsApi.AsyncLoad
        let! file = getCat json.File

        maybe {
            let! message = context.Update.Message         
            let fileResult = ("", file) |> FileToSend.File        
    
            let sendCat id file =
                if json.File.EndsWith ".gif" then
                    sendDocument id file "" |> cast
                else
                    sendPhoto id file "" |> cast

            sendCat message.Chat.Id fileResult |> execute context 
        } |> ignore
    } 
    |> Async.Catch
    |> Async.Ignore
    |> Async.Start
    
let update context = 
    processCommands context [
        cmd "/meow" onMeow
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