module AgsXmppWrapper

open agsXMPP
open agsXMPP.protocol.client
open agsXMPP.protocol.extensions.pubsub
open agsXMPP.protocol.iq.search
open agsXMPP.Xml.Dom
open System
open System.Threading.Tasks
open NLog

type AgsXmppWrapper(username:string, password:string, server:string) = 
    let client = new XmppClientConnection("localhost")
    let nlog = LogManager.GetLogger("default")
    let pubsubManager = new PubSubManager(client)

    do
        client.OnError
            |> Observable.add (fun ex -> nlog.Error(ex))
        client.OnReadXml
            |> Observable.add (fun xml -> nlog.Trace("<<< " + xml))
        client.OnWriteXml
            |> Observable.add (fun xml -> nlog.Trace(">>> " + xml))
        client.OnPresence
            |> Observable.add (fun presence -> 
                match presence with 
                | p when p.From <> client.MyJID && p.Type = PresenceType.unavailable -> 
                    nlog.Info(sprintf "%O has gone offline" p.From)
                | p when p.From <> client.MyJID -> 
                    nlog.Info(sprintf "%O is online" p.From)
                | _ -> ()
            )
        client.OnMessage
            |> Observable.add (fun msg -> nlog.Info(sprintf "Message received: %O" msg))

        nlog.Info(sprintf "Configured with user %s@%s and password %s" username server password)

    let userIsAlreadyRegistered xmlError = 
        
        let desc =
            xmlError
            |> Xml.descendants
        let conflictNode =
            xmlError
            |> Xml.descendants
            |> Seq.tryFind (fun x -> x.TagName.ToLowerInvariant() = "conflict")
        
        match conflictNode with
        | Some _ -> true
        | None -> false


    let ensureAccountExists () = async {
        let taskSource = new TaskCompletionSource<unit>()
        client.RegisterAccount <- true

        let registeredSuccessfully () = 
            nlog.Info("Registered succesfully")
            taskSource.SetResult(())

        client.OnRegisterError
            |> Observable.subscribeToOne (fun xmlError -> 
                if not (userIsAlreadyRegistered xmlError) then
                    nlog.Error(sprintf "Registration failed: %O" xmlError)
                    taskSource.SetException(new Exception())
                else
                    registeredSuccessfully ()
            )

        client.OnRegistered
            |> Observable.subscribeToOne (fun x -> registeredSuccessfully ())

        nlog.Info("Ensuring account exists..")
        client.Open(username, password)

        do! taskSource.Task |> Async.AwaitTask 
    }
        

    let loginToAccount () = async {
        client.RegisterAccount <- false
        client.AutoPresence <- true
        client.AutoRoster <- true

        let taskSource = new TaskCompletionSource<unit>()

        client.OnAuthError
            |> Observable.subscribeToOne (fun xmlError -> 
                nlog.Error(sprintf "Authentication failed: %O" xmlError)
                taskSource.SetException(new Exception())
            )

        client.OnLogin
            |> Observable.subscribeToOne (fun err -> 
                nlog.Info("Logged in succesfully")
                taskSource.SetResult(())
                client.RequestRoster()
                client.SendMyPresence()
            )

        nlog.Info("Logging in to account..")
        client.Open(username, password)
        
        do! taskSource.Task |> Async.AwaitTask 
    }

    let getSearchForAllUsersElement = 
        let search = new SearchIq(IqType.set, new Jid("search.localhost"), new Jid(sprintf "%s@%s" username server))

        //add the base x element
        let xml = """
            <x xmlns='jabber:x:data' type='submit'>
              <field type='hidden' var='FORM_TYPE'>
                <value>jabber:iq:search</value>
              </field>
              <field var="search">
                <value>*</value>
            </field>
              <field var="Username">
                  <value>1</value>
              </field>
              <field var="Email">
                  <value>1</value>
              </field>
              <field var="Name">
                  <value>1</value>
              </field>
            </x>
        """
        
        search.Query.InnerXml <- xml

        search

    member this.Jid = client.MyJID

    member this.Authenticate () = async {
        do! ensureAccountExists ()
        do! loginToAccount ()
        ()
    }

    member this.createPubSubNode nodeName = async {
        let nodeAddress = new Jid(client.MyJID.Bare)
        nlog.Info(sprintf "Creating pubsub node %O%s..." nodeAddress nodeName)
        let completionSource = new TaskCompletionSource<unit>()

        pubsubManager.CreateNode(nodeAddress, nodeName, true, fun _ (iq:IQ) _ -> 
            if iq.Error <> null then
                nlog.Error(sprintf "Failed to create pubsub node: %O" (iq.Error))
                completionSource.SetException(new Exception())
            else
                nlog.Info("Created pubsub node.")
                completionSource.SetResult(())
        )
        
        do! completionSource.Task
            |> Async.AwaitTask

        return ()
    }

    member this.subscribe nodeAddress nodeName = async {
        nlog.Info(sprintf "Subscribing to %O/%s..." nodeAddress nodeName)
        let completionSource = new TaskCompletionSource<unit>()

        pubsubManager.Subscribe(new Jid(server), nodeAddress, nodeName, fun _ iq _ -> 
            nlog.Info("Subscribed successfully.")
            completionSource.SetResult(())
        )

        return! completionSource.Task
            |> Async.AwaitTask
    }

    member this.publish nodeAddress nodeName message = async {
        nlog.Info(sprintf "Publishing to %O/%s..." nodeAddress nodeName)
        let completionSource = new TaskCompletionSource<unit>()

        pubsubManager.PublishItem(nodeAddress, nodeName, new Item(InnerXml = sprintf "<msg>%s</msg>" message), fun _ jiq _ ->
            nlog.Info("Published successfully.")
            completionSource.SetResult(())
        )

        return! completionSource.Task
            |> Async.AwaitTask
    }
    

    member this.searchForAllUsers () = async {
        let completionSource = new TaskCompletionSource<list<Jid>>()
        
        client.OnIq 
            |> Observable.filter (fun x -> 
                not (
                    x 
                    |> Xml.descendants 
                    |> Seq.filter (fun x -> x.Namespace = "jabber:iq:search" && x.TagName = "query")
                    |> Seq.isEmpty
                )
            )
            |> Observable.subscribeToOne (fun searchXml -> 
                nlog.Info(sprintf "User search was successful.")

                let searchResults = 
                    searchXml.Query 
                    |> Xml.descendants 
                    |> Seq.filter (fun x -> x.TagName = "field" && x.Attribute("var") = "jid" && x.FirstChild <> null)
                    |> Seq.map (fun x -> new Jid(x.FirstChild.Value))
                    |> Seq.toList

                completionSource.SetResult(searchResults)
            )

//        let search2 = new SearchIq(IqType.get, new Jid("search.localhost"), new Jid(sprintf "%s@%s" username server))
//        client.Send(search2)
        let search = getSearchForAllUsersElement

        nlog.Info("Searching for users..")
        client.Send(search)
        
        return! completionSource.Task |> Async.AwaitTask
    }

    member this.addToRoster (id:Jid) = 
        client.PresenceManager.Subscribe(id)
        client.RosterManager.AddRosterItem(id)
        client.PresenceManager.ApproveSubscriptionRequest(id)