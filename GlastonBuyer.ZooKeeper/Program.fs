open System
open agsXMPP
open AgsXmppWrapper

let nlog = NLog.LogManager.GetLogger("default")

let subscribeToAllMonkeys (xmpp:AgsXmppWrapper) = async {
    nlog.Info("Searching for monkeys..")

    let! users = xmpp.searchForAllUsers ()
    let monkeys = 
        users
        |> Seq.filter (fun x -> x.User.IndexOf("spammonkey", StringComparison.InvariantCultureIgnoreCase) > -1)

    nlog.Info(sprintf "Found %i monkeys, subscribing.." (monkeys |> Seq.length))

    monkeys
        |> Seq.iter (fun x -> xmpp.addToRoster(x))

    nlog.Info("Subscribed to monkeys")
}


let runZookeeper = async {
    let xmpp = new AgsXmppWrapper.AgsXmppWrapper("zookeeper", "secret11", "localhost")
    do! xmpp.Authenticate ()

    //do! xmpp.createPubSubNode "monkeydo" //ringmaster -> monkey node
    //do! xmpp.createPubSubNode "monkeysay"  //monkeys -> ringmaster node
    do! xmpp.subscribe (new Jid(xmpp.Jid.Bare)) "monkeysay"
    do! xmpp.publish (new Jid(xmpp.Jid.Bare)) "monkeysay" "HI THERE"
    
    //do! subscribeToAllMonkeys xmpp
}

[<EntryPoint>]
let main argv = 
    runZookeeper
        |> Async.RunSynchronously

    Console.ReadLine() |> ignore
    0
