module Program

open System
open Funogram.Api
open Funogram.Bot
open Funogram.Types
open FSharp.Data
open ExtCore.Control
open Funogram.RequestsTypes
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
    let cache = ConcurrentBag<_>()
    fun () ->
        async {
            if Interlocked.Increment(currentSize) <= cacheSize then
                let! json = ApiUrl |> CatsApi.AsyncLoad
                let file = json.File
                cache.Add(file)
                return file
            else
                let (_, result) = cache.TryPeek()
                return result
        }

let onMeow context =
    async {
        sendChatAction context.Update.Message.Value.Chat.Id ChatAction.UploadPhoto 
        |> execute context

        let! file = getCat()

        maybe {
            let! message = context.Update.Message         
            let fileUri = Uri(file) |> FileToSend.Url       
    
            let sendCat id =
                if file.EndsWith ".gif" then
                    sendDocument id fileUri "" |> cast
                else
                    sendPhoto id fileUri "" |> cast

            sendCat message.Chat.Id |> execute context 
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