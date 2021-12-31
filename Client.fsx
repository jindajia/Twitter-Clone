
#r "nuget: Suave, 2.6.1"
#r "nuget: FSharp.Data, 4.2.5"
#r "nuget: FSharp.Json, 0.4.1"

open System
open FSharp.Core
open FSharp.Data
open System.Net.WebSockets
open System.Threading;  
open System.Text
open FSharp.Json

let webSocket = new ClientWebSocket()
let uri = Uri("ws://localhost:8083/websocket")


let connectToServer = 
    if webSocket.State <> WebSocketState.Open then
        let a = webSocket.ConnectAsync(uri, CancellationToken.None)
        a.Wait()

type SocketMessage = {
    Operation : string
    Username : string
    Password : string
}

type JsonMessage = 
  | Register of string * string
  | Login of string * string
  | Tweet of string * string * List<string> * List<string> 
  | Response of string * string * string

let send (str:string) = async{
    printfn "Starting Send"
    let serialization =Encoding.UTF8.GetBytes str |> ArraySegment<byte>
    let a = webSocket.SendAsync(serialization, WebSocketMessageType.Text, true, CancellationToken.None)
    a.Wait()
    }

let receivefun = async{
    printfn "Start Receive"
    let buffer = WebSocket.CreateClientBuffer(1000, 1000)
    let mutable dd = null
    while(true) do
        let result = webSocket.ReceiveAsync(buffer, CancellationToken.None);
        result.Wait()
        let arr = buffer|> Seq.toArray
        let json = (Encoding.UTF8.GetString(arr, 0, result.Result.Count))
        let response = Json.deserialize<JsonMessage> json
        match response with
        |Response(status, action, username) ->
            printfn "%s %s %s" status action username
        |_->()
    }

let register (username:string) (password:string) = async{
    printfn "Starting Register"
    let register = Register(username, password)
    let json = Json.serialize register
    let serialization =Encoding.UTF8.GetBytes json |> ArraySegment<byte>
    let a = webSocket.SendAsync(serialization, WebSocketMessageType.Text, true, CancellationToken.None)
    a.Wait()
}

let login (username:string) (password:string) = async{
    printfn "Starting Login"
    let message = Login(username, password)
    let json = Json.serialize message
    let serialization =Encoding.UTF8.GetBytes json |> ArraySegment<byte>
    let a = webSocket.SendAsync(serialization, WebSocketMessageType.Text, true, CancellationToken.None)
    a.Wait()
}

let sendTweet (username:string) (content:string) (hashTags:List<string>) (mentions:List<string>) = async{
    printfn "Starting Send tweet"
    let message = Tweet(username, content, hashTags, mentions)
    let json = Json.serialize message
    let serialization =Encoding.UTF8.GetBytes json |> ArraySegment<byte>
    let a = webSocket.SendAsync(serialization, WebSocketMessageType.Text, true, CancellationToken.None)
    a.Wait()
}


connectToServer
[login "jiajin" "jinda";sendTweet "jiajin" "Hello every one" [] ["jiajin"];receivefun]|>Async.Parallel |> Async.RunSynchronously |>ignore
System.Console.ReadLine() |> ignore