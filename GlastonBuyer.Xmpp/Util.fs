module Util

open System
open System.Collections

let ofType<'T> (enumerable:IEnumerable) = seq {
        for i in enumerable do if i :? 'T then yield i :?> 'T
    }