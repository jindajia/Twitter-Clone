#r "nuget: Suave, 2.6.1"
#r "nuget: FSharp.Data, 4.2.5"
#r "nuget: FSharp.Json, 0.4.1"

open System
open System.IO
open FSharp.Core
open FSharp.Data
open System.Net.WebSockets
open System.Threading;  
open System.Text
open FSharp.Json
open System.Security.Cryptography

let encrypt (publicKey:string) (content:string)=
    let rsa = new RSACryptoServiceProvider()
    rsa.FromXmlString(publicKey)
    let cipherBytes = rsa.Encrypt((Encoding.UTF8.GetBytes(content)),false)
    Convert.ToBase64String(cipherBytes)

let mutable publicKey = ""

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

let webSocket = new ClientWebSocket()
let uri = Uri("ws://localhost:8080/websocket")
let cts = new CancellationTokenSource()

let mutable myTweets:List<Tweet> = List.Empty
let mutable receivedTweets:List<Tweet> = List.Empty
let mutable username=""
let mutable logined=false

let strContainsOnlyNumber (s:string) = s |> Seq.forall Char.IsDigit

let sendToServer (message:ClientMessage) = async{
    printfn "Send to Server"
    let json = Json.serialize message
    let serialization =Encoding.UTF8.GetBytes json |> ArraySegment<byte>
    let a = webSocket.SendAsync(serialization, WebSocketMessageType.Text, true, cts.Token)
    a.Wait()
}

let connectToServer = 
    if webSocket.State <> WebSocketState.Open then
        let a = webSocket.ConnectAsync(uri, cts.Token)
        a.Wait()

let rec Menu (value:string) =
    match value with
    |"MainMenu"->
        System.Console.Clear()
        printfn "MainMenu"
        printfn "1.Register 2.Login 3.Exit"
        let res = System.Console.ReadLine()
        match res with
        |"1"->Menu "Register"
        |"2"->Menu "Login"
        |"3"->printfn "Exit"
        |_->
            printfn "Input Error"
            Menu "MainMenu"
    |"Register"->
        printfn "Register"
        printfn "Please Input UserName"
        let username = System.Console.ReadLine()
        printfn "%s" username
        printfn "Please Input Password"
        let password = System.Console.ReadLine()
        let encUsername = encrypt publicKey username
        let encPassword = encrypt publicKey password
        sendToServer(Register(encUsername,encPassword))|>Async.RunSynchronously
        System.Console.ReadLine() |>ignore
        Menu "MainMenu"
    |"Login"->
        printfn "Start Login"
        printfn "Please Input UserName"
        let username = System.Console.ReadLine().Trim()
        printfn "%s" username
        printfn "Please Input Password"
        let password = System.Console.ReadLine()
        sendToServer(Login(username,password))|>Async.RunSynchronously
        System.Console.ReadLine() |>ignore
        if logined then Menu "LoginMenu"
        else Menu "MainMenu"
    |"LoginMenu"->
        System.Console.Clear()
        printfn "LoginMenu Username:%s " username
        printfn "1.MyTweets 2.ReceivedTweets 3.SendTweet 4.Subscribe 5.QueryTweets 6.Exit"
        let res = System.Console.ReadLine()
        match res with
        |"1"->Menu "MyTweets"
        |"2"->Menu "ReceivedTweets"
        |"3"->Menu "SendTweet"
        |"4"->Menu "FollowUser"
        |"5"->Menu "QueryTweets"
        |"6"->printfn "Exit"
        |_->
            printfn "Input Error"
            Menu "LoginMenu"
    |"QueryTweets"->
        System.Console.Clear()
        printfn "Query Tweets"
        printfn "1.All Related Tweets 2.Query by Tag 3.Query by Mentioned"
        let res = System.Console.ReadLine()
        match res with
        |"1"->
            receivedTweets|>List.iteri (fun i tweet->printfn"%d %s" (i+1) (tweet.ToString()))
            System.Console.ReadLine()|>ignore
            Menu "LoginMenu"
        |"2"->
            printfn "Please Input tag"
            let tag = System.Console.ReadLine()
            receivedTweets|>List.filter(fun tweet-> tweet.Tags|>List.exists(fun t->t.Contains(tag)))|>List.iteri (fun i tweet->printfn"%d %s" (i+1) (tweet.ToString()))
            System.Console.ReadLine()|>ignore
            Menu "LoginMenu"
        |"3"->
            receivedTweets|>List.filter(fun tweet-> tweet.Mentions|>List.exists(fun mention->mention=username))|>List.iteri (fun i tweet->printfn"%d %s" (i+1) (tweet.ToString()))
            System.Console.ReadLine()|>ignore
            Menu "LoginMenu"
        |_->()
    |"FollowUser"->
        System.Console.Clear()
        printfn "Subscribe New User:"
        printfn "Who Do You Want To Follow? Please Input UserName"
        let subscribe = System.Console.ReadLine()
        sendToServer(Subscribe(username,subscribe))|>Async.RunSynchronously
        System.Console.ReadLine() |>ignore
        Menu "LoginMenu"
    |"SendTweet"->
        System.Console.Clear()
        printfn "SendTweet:"
        printfn "Please Input Content"
        let content = System.Console.ReadLine()
        printfn "Please Input Tags, use ',' to Split"
        let tags = System.Console.ReadLine()|>(fun str-> str.Trim().Split(','))|>Seq.map(fun tag-> (tag.Trim()))|>Seq.filter(fun str-> str<>"")|>Seq.toList
        printfn "Please Input Mentions, use ',' to Split"
        let mentions = System.Console.ReadLine()|>(fun str-> str.Trim().Split(','))|>Seq.map(fun tag-> (tag.Trim()))|>Seq.filter(fun str-> str<>"")|>Seq.toList
        let tweet = {defaultTweet with Author=username;Time=DateTime.Now; Content=content; Tags=tags; Mentions = mentions}
        sendToServer(SendTweet(tweet))|>Async.RunSynchronously
        System.Console.ReadLine() |>ignore
        Menu "LoginMenu"
    |"MyTweets"->
        printfn"MyTweets"
        myTweets|>List.iteri (fun i tweet->printfn"%d %s" i (tweet.ToString()))
        System.Console.ReadLine() |>ignore
        Menu "LoginMenu"
    |"ReceivedTweets"->
        printfn "ReceivedTweets"
        receivedTweets|>List.iteri (fun i tweet->printfn"%d %s" (i+1) (tweet.ToString()))
        printfn "Input the index you want to reTweet"
        let numStr = System.Console.ReadLine().Trim()
        if numStr.Length>0 && strContainsOnlyNumber numStr then
            let num = numStr|>int
            if num>0&&num<=receivedTweets.Length then
                let tweet = receivedTweets.[num-1]
                let reTweet = {tweet with Author = username}
                sendToServer(SendTweet(reTweet))|>Async.RunSynchronously
                printfn "Retweet Succeed"
                printfn "%s" (reTweet.ToString())
                System.Console.ReadLine()|>ignore
        Menu "LoginMenu"
    |_->()

let receivefun = async {
    printfn "Start Receive"
    let buffer = WebSocket.CreateClientBuffer(1000, 1000)
    let mutable loop = true
    while(loop) do
        let result = webSocket.ReceiveAsync(buffer, cts.Token);
        result.Wait()
        let arr = buffer|> Seq.toArray
        let json = (Encoding.UTF8.GetString(arr, 0, result.Result.Count))
        let serverMessage = Json.deserialize<ServerMessage> json  
        match serverMessage with
        |RegisterRespond(succeed, res)->
            if succeed=true then
                printfn "Register Succeed"
            else
                printfn "Register Failed"
        |LoginRespond(succeed,un,rsa)->
            logined<-succeed
            username<-un
            if succeed=true then
                printfn "Login Succeed"
            else
                printfn "Login Failed"
        |MyTweets(tweetList)->
            myTweets <- tweetList
            printfn "MyTweets:"
            List.iter (fun tweet-> printfn"%s" (tweet.ToString())) myTweets
        |FollowingTweets(tweetList)->
            printfn "Received Tweets:"
            receivedTweets <- tweetList
            List.iter (fun tweet-> printfn"%s" (tweet.ToString())) receivedTweets
        |RSAPublicKey(str)->
            printfn "Received Public Key"
            publicKey<-str
    }

connectToServer

Async.Start(receivefun,cts.Token)

Menu "MainMenu"

// Async.Start(send ("Hello Server"),cts.Token)
// Async.Start(register "jiajinda" "123", cts.Token)
// Async.Start(sendToServer(Login("jiajinda","123")), cts.Token)
// Async.Start(sendToServer (SendTweet({defaultTweet with Content = "Hello Jinda"})))

System.Console.ReadLine() |> ignore
cts.Cancel()