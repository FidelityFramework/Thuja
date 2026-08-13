namespace Thuja.Elements

open Thuja
open Thuja.View
open Thuja.Styles

open Helpers

type internal List =
  { Items: string list 
    Index: int }
  interface IElement with
    member this.Render(region : Region) : Buffer = 
      let page = this.Index / region.Height

      let items =
        this.Items
        |> Seq.map ^fun item ->
            item.TruncateWithEllipsis region.Width
        |> Seq.skip (page * region.Height)
        |> Seq.truncate region.Height

      let buffer = Buffer.create region.Width region.Height

      for index, item in Seq.indexed items do
        let style =
          if this.Index = page * region.Height + index
          then { Style.defaultStyle with Attributes = [ Attribute.Invert ] }
          else Style.defaultStyle

        Buffer.writeText buffer style item 0 index

      buffer 

[<AutoOpen>]
module List = 
  let list items index (region : Region) : ViewTree =
    ViewTree.create 
      { Items = items; Index = index }
      region
      []