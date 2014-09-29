module Xml

open agsXMPP.Xml.Dom

let rec descendants (node:Element) = 
    node :: (
        node.ChildNodes
        |> Util.ofType<Element>
        |> Seq.toList
        |> List.fold (fun acc x -> acc @ (descendants x)) (List.empty<Element>)
    )

