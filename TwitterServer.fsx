#r "nuget: Suave, 2.6.1"
#r "nuget: Akka.FSharp, 1.4.27"
#r "nuget: FSharp.Json, 0.4.1"

open System
open System.Threading
open System.Security.Cryptography
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
open System.Text

let encrypt (publicKey:string) (content:string)=
    let rsa = new RSACryptoServiceProvider()
    rsa.FromXmlString(publicKey)
    let cipherBytes = rsa.Encrypt((Encoding.UTF8.GetBytes(content)),false)
    Convert.ToBase64String(cipherBytes)

let decrypt (privateKey:string) (content:string)=
    let rsa = new RSACryptoServiceProvider()
    rsa.FromXmlString(privateKey)
    let cipherBytes = rsa.Decrypt((Convert.FromBase64String(content)),false)
    Encoding.UTF8.GetString(cipherBytes)

let rsa = new RSACryptoServiceProvider()
let privateKey = rsa.ToXmlString(true)
let publicKey = rsa.ToXmlString(false)

//Twitter
let system = ActorSystem.Create("TwitterServer")
let cts = new CancellationTokenSource()
let conf = { defaultConfig with cancellationToken = cts.Token }

//json serialize************************************
//Client to Server
type Tweet = 
    {
        mutable Author: string //username of Author
        mutable Time: DateTime //dateTime of sending
        mutable Content: string
        mutable Tags: List<string>
        mutable Mentions: List<string> //mentioned users
    }

let defaultTweet = {Author=""; Time = DateTime.Now;Content="";Tags=List.Empty;Mentions=List.Empty}
//Client to Server
type ClientMessage = 
    | Register of string * string
    | Login of string * string
    | SendTweet of Tweet
    | Subscribe of string * string
//Server to Client  
type ServerMessage =
    | RegisterRespond of bool * string
    | LoginRespond of bool * string * string //succed * username * RSA
    | MyTweets of List<Tweet>
    | FollowingTweets of List<Tweet>
    | RSAPublicKey of string
//json serialize************************************

//ws to MainActor
type MainActorMessage =
    | Register of string * string * WebSocket
    | Login of string * string * WebSocket
    | SendTweet of Tweet * WebSocket
    | Subscribe of string * string * WebSocket

//MainActor/UserActor to UserActor
type UserActorMessage = 
    | LoginInitialize of WebSocket
    | SendTweet of Tweet  * Map<string,IActorRef> * WebSocket
    | AddFollower of string * IActorRef //follower; follower's actor
    | ReceiveTweet of Tweet
    | ReceiveTweets of List<Tweet>
    | QueryAllTweets
    | QueryByTag of string
    | QueryByMention of string

let clientSerialize (message:ServerMessage) =
    let result = 
        Json.serialize (message)
        |> System.Text.Encoding.UTF8.GetBytes
        |> ByteSegment
    result


//UserActor
let UserActor (username:string) (mailbox: Actor<UserActorMessage>) = 
    let mutable followersMap : Map<string, IActorRef> = Map.empty
    let mutable myTweets : List<Tweet> = List.Empty
    let mutable followingTweets : List<Tweet> = List.Empty
    let mutable tweetsFilter:Set<Tweet> = Set.empty// Eliminate duplicate tweets 
    let mutable socketList : List<WebSocket> = List.Empty
    let rec loop () = actor {        
        let! msg = mailbox.Receive ()
        let sender = mailbox.Sender()
        
        match msg  with
        | LoginInitialize(webSocket) ->
            socketList <-[webSocket]
            webSocket.send Text (clientSerialize (MyTweets(myTweets))) true|>Async.RunSynchronously|>ignore
            webSocket.send Text (clientSerialize (FollowingTweets(followingTweets))) true|>Async.RunSynchronously|>ignore
        | SendTweet(tweet,mentionsMap, webSocket) ->
            myTweets <- myTweets@[tweet]
            followersMap |> Map.iter (fun k v -> v<!ReceiveTweet(tweet))
            mentionsMap |> Map.filter (fun mention actor-> (followersMap.ContainsKey(mention)<>true))|>Map.iter (fun m actor-> actor <!ReceiveTweet(tweet))
            webSocket.send Text (clientSerialize (MyTweets(myTweets))) true|>Async.RunSynchronously|>ignore
        | ReceiveTweets(tweets) ->
            tweets|>List.filter(fun tweet-> tweetsFilter.Contains(tweet)<>true)|>List.iter(fun tweet-> tweetsFilter<-tweetsFilter.Add(tweet); followingTweets<-followingTweets@[tweet])
            if socketList.IsEmpty<>true then
                let webSocket = socketList.Head
                webSocket.send Text (clientSerialize (FollowingTweets(followingTweets))) true|>Async.RunSynchronously|>ignore
        | ReceiveTweet(tweet)->
            if tweetsFilter.Contains(tweet)<>true then
                tweetsFilter<-tweetsFilter.Add(tweet)
                followingTweets <- followingTweets@[tweet]
                if socketList.IsEmpty<>true then
                    let webSocket = socketList.Head
                    webSocket.send Text (clientSerialize (FollowingTweets(followingTweets))) true|>Async.RunSynchronously|>ignore
        | AddFollower(follower, actor)->
            followersMap <- followersMap.Remove(follower).Add(follower,actor)
            actor<!ReceiveTweets(myTweets)
        | QueryAllTweets -> ()
        | QueryByMention(mention) ->()
        | QueryByTag(tag)->()
        | _->()
        return! loop()     
    }
    loop ()

//MainActor, taking charge of Register and login
let MainActor (mailbox: Actor<MainActorMessage>) = 
    let mutable passMap : Map<string, string> = Map.empty //password map
    let mutable actorMap : Map<string, IActorRef> = Map.empty
    let mutable onlineUserMap : Map<string, WebSocket> = Map.empty //Login in user map
    let mutable followingMap : Map<string, List<string>> = Map.empty //users followed by key user
    let rec loop () = actor {        
        let! msg = mailbox.Receive ()
        let sender = mailbox.Sender()
        match msg  with
        | Register(username, password, webSocket)->
          if passMap.ContainsKey(username) then
            printfn "Username already exist"
            webSocket.send Text (clientSerialize (RegisterRespond(false,"Username already exist"))) true|>Async.RunSynchronously|>ignore
          else
            printfn "Not Contain key"
            passMap <- passMap.Add(username, password)
            let userActor = spawn system username (UserActor username)
            actorMap <- actorMap.Add(username, userActor)
            followingMap <- followingMap.Add(username, List.Empty)
            webSocket.send Text (clientSerialize (RegisterRespond(true,"Username "+ username+" create succeed"))) true|>Async.RunSynchronously |>ignore
        | Login(username, password, webSocket)-> 
          if passMap.ContainsKey(username) && passMap.[username] = password then
            onlineUserMap <- onlineUserMap.Remove(username).Add(username, webSocket)
            actorMap.[username]<!(LoginInitialize webSocket)
            webSocket.send Text (clientSerialize (LoginRespond(true, username,"Login succeed"))) true|>Async.RunSynchronously |>ignore
          else
            printfn "password incorrect"
            webSocket.send Text (clientSerialize (LoginRespond(false, username,"Login failed"))) true |>Async.RunSynchronously |>ignore
        | MainActorMessage.SendTweet(tweet, webSocket)->
            let userActor = actorMap.[tweet.Author]
            let mentionsMap = tweet.Mentions|>List.map(fun m-> m, actorMap.[m])|> Map.ofSeq
            userActor<!SendTweet({tweet with Time=DateTime.Now},mentionsMap,webSocket)
        | MainActorMessage.Subscribe(username,subscribe,webSocket)->
            let userActor = actorMap.[username]
            let subscribeActor = actorMap.[subscribe]
            if subscribeActor<>null then
                subscribeActor<! AddFollower(username, userActor)
        return! loop()     
    }
    loop ()

let mainActor = spawn system "MainActor" MainActor

let ws (webSocket : WebSocket) (context: HttpContext) =
    socket {
        let mutable loop = true
        printfn "Start Connecting"
        do! webSocket.send Text (clientSerialize (RSAPublicKey(publicKey))) true
        while loop do
            let! msg = webSocket.read()

            match msg with
            | (Text, data, true) ->
                printfn "%s" (DateTime.Now.ToString())
                let json = UTF8.toString data
                printfn "%s" json
                let mainMessage = Json.deserialize<ClientMessage> json
                match mainMessage with
                |ClientMessage.Register(encUsername, encPassword)->
                    let username = decrypt privateKey encUsername
                    let password = decrypt privateKey encPassword
                    printfn "Register %s" username
                    mainActor<!Register(username,password,webSocket)
                |ClientMessage.Login(username, password)->
                    printfn "Login %s" username
                    mainActor<!Login(username,password,webSocket)
                |ClientMessage.SendTweet(tweet)->
                    mainActor<!MainActorMessage.SendTweet(tweet,webSocket)
                |ClientMessage.Subscribe(username, subscribe)->
                    mainActor<!MainActorMessage.Subscribe(username,subscribe,webSocket)
                |_->()
            | (Close, _, _) ->
                let emptyResponse = [||] |> ByteSegment
                do! webSocket.send Close emptyResponse true
                loop <- false

            | _ -> ()
    }

let wsWithErrorHandling (webSocket : WebSocket) (context: HttpContext) = 
   
   let exampleDisposableResource = { new IDisposable with member __.Dispose() = printfn "Resource needed by websocket connection disposed" }
   let websocketWorkflow = ws webSocket context
   
   async {
    printfn "Error"
    let! successOrError = websocketWorkflow
    match successOrError with
    // Success case
    | Choice1Of2() -> ()
    // Error case
    | Choice2Of2(error) ->
        // Example error handling logic here
        printfn "Error: [%A]" error
        exampleDisposableResource.Dispose()
        
    return successOrError
   }

let app =
  choose
    [   path "/websocket" >=> handShake ws
        path "/websocketWithError" >=> handShake wsWithErrorHandling
        GET >=> choose
            [ path "/hello" >=> OK "Hello GET"
              path "/goodbye" >=> OK "Good bye GET" ]
        POST >=> choose
            [ path "/hello" >=> OK "Hello POST"
              path "/goodbye" >=> OK "Good bye POST" ]
    ]

let listening, server = startWebServerAsync conf app

Async.Start(server, cts.Token)
printfn "Make requests now"

Console.ReadKey true |> ignore
cts.Cancel()