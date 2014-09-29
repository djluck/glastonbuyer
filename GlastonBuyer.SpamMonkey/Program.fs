open System
open agsXMPP
open AgsXmppWrapper

let nlog = NLog.LogManager.GetLogger("default")
let isZookeeper (jid:Jid) = jid.User.Equals("zookeeper", StringComparison.OrdinalIgnoreCase)
    
let rec subscribeToZookeeper (xmpp:AgsXmppWrapper) = async {
    nlog.Info("Attempting to subscribe to the zookeeper..")
    let sleepFor = 1000 * 10
    let! users = xmpp.searchForAllUsers ()
    let ringmaster = users |> Seq.tryFind (isZookeeper)

    match ringmaster with
    | Some ringmaster -> 
        xmpp.addToRoster ringmaster
        nlog.Info("Subscribed to the zookeeper!")
        return ()
    | None -> 
        nlog.Info("Could not find the zookeeper. Sleeping..")
        do! Async.Sleep sleepFor
        do! subscribeToZookeeper xmpp
}

let runSpamMonkey = async {
    let xmpp = new AgsXmppWrapper("spammonkey-" + Util.uniqueName.Value, "secret11", "localhost")

    do! xmpp.Authenticate ()
    do! subscribeToZookeeper xmpp
}

[<EntryPoint>]
let main argv = 
    //let response = getOneResponseFromMany { Verb = "GET"; Url = "http://www.google.co.uk"; EntityBody = None; Headers = None }
    //printfn "Response: %O" response
    runSpamMonkey 
        |> Async.RunSynchronously

    Console.ReadLine() |> ignore
    0
