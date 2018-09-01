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
    method
    |> api context.Config
    |> Async.Ignore
    |> Async.Start

let onMeow context =
    maybe {
        let json = ApiUrl |> CatsApi.Load
        
        let! message = context.Update.Message
        let file = Uri json.File |> FileToSend.Url

        sendPhoto message.Chat.Id file "" |> execute context
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