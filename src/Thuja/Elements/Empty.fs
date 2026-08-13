namespace Thuja.Elements

open Thuja.View

type internal Empty private () =
  interface IElement with
    member _.Render(_ : Region) : Buffer = 
      Buffer.create 0 0
    member _.Equals(obj : obj, _) : bool = 
      obj :? Empty
    member this.GetHashCode _ : int = 
      hash this
  end
    static member singleton = Empty()

module ViewTree =
  let empty = ViewTree.create Empty.singleton Region.empty []
  
[<AutoOpen>]
module Empty =
  let empty (_ : Region) : ViewTree =
    ViewTree.create
      Empty.singleton
      Region.empty
      []