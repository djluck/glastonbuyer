module Observable 

open Microsoft.FSharp.Control
open System

let subscribeToOne callback observable = 
    let toDispose = ref (null :> IDisposable)
    toDispose := 
        observable
        |> Observable.subscribe (fun x ->
            callback x
            toDispose.contents.Dispose()
        )
    ()
    

