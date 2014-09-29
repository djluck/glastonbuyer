module Util

open System
open System.Net.NetworkInformation
open System.Security.Cryptography
open System.Text
open System.Collections

let uniqueName = lazy (
        let allMacAddressBytes = 
            NetworkInterface.GetAllNetworkInterfaces()
            |> Seq.fold 
                (fun acc next -> 
                    acc @ (next.GetPhysicalAddress().GetAddressBytes() |> Array.toList)
                ) 
                List.empty
            |> Seq.toArray

        sprintf "%s-%s" (System.Environment.MachineName) (Convert.ToBase64String(MD5.Create().ComputeHash(allMacAddressBytes)))
    )