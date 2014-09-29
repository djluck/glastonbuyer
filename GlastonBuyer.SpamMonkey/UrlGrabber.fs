module UrlGrabber


open System
open System.IO
open System.Net
open System.Text
open System.Threading

let httpTimeout = 60
let workerMaxLimit = 60
let workerSpinUpDelay = TimeSpan.FromSeconds(1.0)

type Request = { 
    Verb:string
    Url:string
    EntityBody: string Option
    Headers: Map<string, string> Option
}

let buildHttpRequest (request:Request) = 
    let httpRequest = HttpWebRequest.CreateHttp(request.Url) 
    httpRequest.Timeout <- httpTimeout
    httpRequest.Method <- request.Verb
    httpRequest.AutomaticDecompression <- DecompressionMethods.None

    if request.EntityBody.IsSome then
        use entityBodyStream = httpRequest.GetRequestStream()
        let entityBodyEncoded = Encoding.UTF8.GetBytes(request.EntityBody.Value)
        entityBodyStream.Write(entityBodyEncoded, 0, entityBodyEncoded.Length)

    httpRequest


let readHttpResponse (httpResponse:WebResponse) = async {
    let buffer = Array.zeroCreate<byte> 8192
    let sb = new StringBuilder()
    use responseStream = httpResponse.GetResponseStream()

    let rec readHttpResponseRecursive = async {
        let! bytesRead = responseStream.AsyncRead(buffer, 0, buffer.Length)
        sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead)) 
            |> ignore

        if bytesRead > 0 then
            do! readHttpResponseRecursive
    }
    
    do! readHttpResponseRecursive

    return sb.ToString()
}


let rec tryAndGetResponse (request:Request) (cancellationSource:CancellationTokenSource) = async {
    try
        let httpRequest = buildHttpRequest request
        use! httpResponse = httpRequest.AsyncGetResponse()
        let! responseBody = readHttpResponse httpResponse

        return Some responseBody
    with 
        | :? WebException as webEx when webEx.Status = WebExceptionStatus.Timeout -> 
            return! tryAndGetResponse request cancellationSource
        | :? OperationCanceledException as ex -> 
            return None
}

let getOneResponseFromMany (request:Request) = 
    use cancellationSource = new CancellationTokenSource()
    let tryAndGetResponseForRequest = tryAndGetResponse request cancellationSource

    [1 .. workerMaxLimit]
        |> Seq.map (fun x -> tryAndGetResponseForRequest)
        |> Async.Parallel
        |> (fun x -> Async.RunSynchronously(x, cancellationToken = cancellationSource.Token))
        |> Seq.find Option.isSome


