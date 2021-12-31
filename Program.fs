// Learn more about F# at http://docs.microsoft.com/dotnet/fsharp

open System
open System.Security.Cryptography
open System.IO
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

let message = "Sunday"

let cipherStr = encrypt publicKey message
let deCipherStr = decrypt privateKey cipherStr
printfn "%s" deCipherStr