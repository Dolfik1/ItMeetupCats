module Program

open System
open Funogram.Api
open Funogram.Bot
open Funogram.Types
open FSharp.Data
open ExtCore.Control

[<Literal>]
let API_URL = "http://aws.random.cat/meow"
type CatsApi = JsonProvider<API_URL>

let onMeow context =
    maybe {
        let json = API_URL |> CatsApi.Load
        
        let! message = context.Update.Message
        let file = new Uri(json.File) |> FileToSend.Url
        
        sendPhoto message.Chat.Id file ""
        |> api context.Config
        |> Async.Ignore
        |> Async.Start
    } |> ignore
    
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