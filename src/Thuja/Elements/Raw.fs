namespace Thuja.Elements

open System.Collections

open Thuja.View

type internal Raw<'model when 'model : equality> =
  { Model: 'model
    Process: 'model -> int * int -> string }
  interface IElement with
    member this.Render(region : Region): Buffer = 
      let buffer = Buffer.create region.Width region.Height

      let raw = this.Process this.Model (region.Width, region.Height)
      buffer.EnqueueRaw (0, 0) raw

      buffer

    member this.Equals(obj : obj, comparer : IEqualityComparer): bool = 
      match obj with
      | :? Raw<'model> as other -> comparer.Equals(this.Model, other.Model)
      | _ -> false

    member this.GetHashCode _: int = 
      hash this.Model

[<AutoOpen>]
module Raw = 
  let raw proc (model : 'model) (region : Region) : ViewTree =
    ViewTree.create 
      { Model = model; Process = proc }
      region
      []