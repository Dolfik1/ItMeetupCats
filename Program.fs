module Program

open System
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

let onMeow context =
    async {
        sendChatAction context.Update.Message.Value.Chat.Id ChatAction.UploadPhoto |> execute context
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
    } |> Async.Catch |> Async.Ignore |> Async.Start
    
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
        } update None
    0