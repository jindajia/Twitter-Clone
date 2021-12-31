#r "nuget: Suave, 2.6.1"
#r "nuget: Newtonsoft.Json, 13.0.1"

open System
open System.IO
open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.Writers
open Newtonsoft.Json
type TZInfo = {tzName: string; minDiff: float; localTime: string; utcOffset: float}

// the function takes uint as input, and we represent that as "()"

let getClosest () = 

    // This gets all the time zones into a List-like object

    let tzs = TimeZoneInfo.GetSystemTimeZones()

    // List comprehension + type inference allows us to easily perform conversions

    let tzList = [

        for tz in tzs do

        // convert the current time to the local time zone

        let localTz = TimeZoneInfo.ConvertTime(DateTime.Now, tz) 

        // Get the datetime object if it was 5:00pm 

        let fivePM = DateTime(localTz.Year, localTz.Month, localTz.Day, 17, 0, 0)

        // Get the difference between now local time and 5:00pm local time.

        let minDifference = (localTz - fivePM).TotalMinutes

        yield {

                tzName=tz.StandardName;

                minDiff=minDifference;

                localTime=localTz.ToString("hh:mm tt");

                utcOffset=tz.BaseUtcOffset.TotalHours;

             }

    ]

    // We use the pipe operator to chain function calls together

    tzList 

        // filter so that we only get tz after 5pm

        |> List.filter (fun (i:TZInfo) -> i.minDiff >= 0.0) 

        // sort by minDiff

        |> List.sortBy (fun (i:TZInfo) -> i.minDiff) 

        // Get the first item

        |> List.head

let app:WebPart =

    choose [ GET >=> choose

        [ 

            // We are getting the closest time zone, converting it to JSON, then setting the MimeType

            path "/" >=> request (fun _ -> OK <| JsonConvert.SerializeObject(getClosest())) >=> setMimeType "application/json; charset=utf-8"

        ]

    ]

let printTotalFileBytesUsingAsync (path: string) =
    async {
        let! bytes = File.ReadAllBytesAsync(path) |> Async.AwaitTask
        let fileName = Path.GetFileName(path)
        printfn $"File {fileName} has %d{bytes.Length} bytes"
    }


let printTotalFileBytes path =
    async {
        let! bytes = File.ReadAllBytesAsync(path) |> Async.AwaitTask
        let fileName = Path.GetFileName(path)
        printfn $"File {fileName} has %d{bytes.Length} bytes"
    }

fsi.CommandLineArgs
|> Seq.map printTotalFileBytes
|> Async.Sequential
|> Async.Ignore
|> Async.RunSynchronously
|> ignore


// let cfg =

//     { defaultConfig with

//         bindings = [ HttpBinding.createSimple HTTP "127.0.0.1" 8090]}
// startWebServer cfg app
