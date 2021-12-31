#r "nuget: Suave, 2.6.1"
#r "nuget: Akka.FSharp, 1.4.27"
#r "nuget: FSharp.Json, 0.4.1"

open System
open System.Threading
open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.RequestErrors
open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket
open Akka.Actor
open Akka.FSharp
open FSharp.Json


type ResponseType = {
  Succeed : bool
  Info : string
}

type Tweet = {
  Username : string
  Content : string
  HashTags : List<string>
  Mentions : List<string>
}

type User = {
  Subscribe : List<User>
  UserName : string
  Password : string
  Actor : IActorRef
}

type SocketMessage = {
    Operation : string
    Username : string
    Password : string
}

type MainActorMessage =
  | ActorRegister of string * string
  | ActorLogin of string * string * WebSocket
  | ActorTweet of string * string * List<string> * List<string>
  | ActorResponse of string * string * string

type JsonMessage = 
  | Register of string * string
  | Login of string * string
  | Tweet of string * string * List<string> * List<string> 
  | Response of string * string * string

let system = ActorSystem.Create("TwitterServer")

//UserActor, each user that have login can be seen as an actor
type Message = 
  | FollowerOnline of string * IActorRef
  | InitInitialization of Map<string, IActorRef> * Map<string, IActorRef>
  | SendTweet of string * string * List<string> * List<IActorRef>
  | ReceiveTweet of string * string * List<string>
  | UpdateSocket of WebSocket
  | QuerySelfTweets
  | QueryFollowingTweets

let UserActor (username:string) (webSocket:WebSocket) (mailbox: Actor<Message>) = 
    let mutable socket = webSocket
    let rec loop () = actor {        
        let! msg = mailbox.Receive ()
        let sender = mailbox.Sender()
        
        match msg  with
        | FollowerOnline(username, actor) ->()
        | InitInitialization(following, follower) ->()
        | SendTweet(username, content, hashTags, actorList) ->
          List.iter (fun actor-> actor<!ReceiveTweet(username, content, hashTags)) actorList
        | ReceiveTweet(username, content, hashTags) ->
            printfn "%s %s %s" username "ReceiveTweet" content
            let byteResponse = 
              Json.serialize (Response(content, "Tweet", username))
              |> System.Text.Encoding.UTF8.GetBytes
              |> ByteSegment
            let task = socket.send Text byteResponse true
            Async.RunSynchronously (task, 1000)|>ignore
        | UpdateSocket(newSocket)->
          socket<- newSocket
        return! loop()     
    }
    loop ()


//MainActor, taking charge of Register and login
let MainActor (mailbox: Actor<MainActorMessage>) = 
    let mutable userMap : Map<string, User> = Map.empty
    let mutable onlineUserMap : Map<string, WebSocket> = Map.empty
    let rec loop () = actor {        
        let! msg = mailbox.Receive ()
        let sender = mailbox.Sender()
        match msg  with
        | ActorRegister(username, password)->
          if userMap.ContainsKey(username) then
            printfn "Contain key"
            sender <? {Succeed = false; Info = username} |> ignore
          else
            printfn "Not Contain key"
            let user = {Subscribe = List.Empty; UserName = username; Password = password; Actor = null}
            userMap <- userMap.Add(username, user)
            sender <? {Succeed = true; Info = username} |> ignore
          return! loop()
        | ActorLogin(username, password, webSocket)-> 
          if userMap.ContainsKey(username) && userMap.[username].Password = password then
            let mutable userActor = userMap.[username].Actor
            if userActor = null then  userActor <- spawn system username (UserActor username webSocket)
            else userActor<!UpdateSocket(webSocket)
            onlineUserMap <- onlineUserMap.Remove(username).Add(username, webSocket)
            let mutable user = userMap.Item username
            user <- {user with Actor = userActor}
            userMap <- userMap.Remove(username).Add(username, user)
            sender <? {Succeed = true; Info = username}|> ignore
          else
            printfn "password incorect; passowrd = %s" userMap.[username].Password
            sender <? {Succeed = false; Info = username} |> ignore
        | ActorTweet(username, content, hashTags, mentions)->
          let user = userMap.[username]
          let mutable actorList:List<IActorRef> = List.Empty
          List.iter (fun x -> actorList<- (userMap.[x].Actor::actorList)) mentions
          user.Actor<!SendTweet(username,content, hashTags, actorList)
        return! loop()     
    }
    loop ()

let mainActor = spawn system "MainActor" MainActor





let ws (webSocket : WebSocket) (context: HttpContext) =
  socket {
    let mutable loop = true
    while loop do
        let! msg = webSocket.read()

        match msg with
        | (Text, data, true) ->
            let json = UTF8.toString data
            let mainMessage = Json.deserialize<JsonMessage> json
            match mainMessage with
            |Register(username, password)->
              printfn "Register %s" username
              let task = mainActor <? ActorRegister(username, password)
              let res:ResponseType = Async.RunSynchronously (task, 1000)
              let mutable status = "Succeed"
              if res.Succeed<>true then
                status <- "Error"
              printfn "%s: %s" status username
              let byteResponse = 
                Json.serialize (Response(status, "Register", username))
                |> System.Text.Encoding.UTF8.GetBytes
                |> ByteSegment
              do! webSocket.send Text byteResponse true
            |Login(username, password)->
              printfn "Login %s" username
              let task = mainActor<? ActorLogin(username, password, webSocket)
              let res:ResponseType = Async.RunSynchronously (task, 1000)
              let mutable status = "Succeed"
              if res.Succeed<>true then
                status <- "Error"
              printfn "%s: %s" status username
              let byteResponse = 
                Json.serialize (Response(status, "Login", username))
                |> System.Text.Encoding.UTF8.GetBytes
                |> ByteSegment
              do! webSocket.send Text byteResponse true
            |Tweet(username, content, hashTags, mentions)->
              mainActor<! ActorTweet(username, content, hashTags, mentions)
            |_->()
        | (Close, _, _) ->
            let emptyResponse = [||] |> ByteSegment
            do! webSocket.send Close emptyResponse true
            loop <- false

        | _ -> ()
  }

let handleRegister (username, password) = request (fun r ->
  let task = mainActor <? Register(username, password)
  let res:ResponseType = Async.RunSynchronously (task, 1000)
  printfn "Register %s %s" username password
  OK (sprintf "Very Good"))

let app =
  choose
    [ path "/websocket" >=> handShake ws
      GET >=> choose
        [ path "/hello" >=> OK "Hello GET"
          path "/goodbye" >=> OK "Good bye GET" ]
      POST >=> choose
        [ pathScan "/register/%s/%s" handleRegister
          path "/hello" >=> OK "Hello POST"
          path "/goodbye" >=> OK "Good bye POST" ] 
    ]


startWebServer {defaultConfig with bindings = [ HttpBinding.createSimple HTTP "127.0.0.1" 8083 ]} app
