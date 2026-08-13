namespace Thuja.Elements

open Thuja
open Thuja.View
open Thuja.Styles

open type Border.Line

type PanelProps =
  | BorderStyle of BorderStyle
  | BorderColor of Color

type internal Panel =
  { Props: PanelProps list }
  interface IElement with
    member this.Render (region : Region): Buffer =
      // props
      let style = 
        this.Props 
        |> Seq.choose ^function | BorderStyle style -> Some style | _ -> None
        |> Seq.tryHead
        |> Option.defaultValue Normal

      let color = 
        this.Props 
        |> Seq.choose ^function | BorderColor color -> Some color | _ -> None
        |> Seq.tryHead
        |> Option.defaultValue Reset

      // lines
      let line symbol = Border.Styles.[style].[symbol].With color

      let buffer = Buffer.create region.Width region.Height
      
      let width = Buffer.length2 buffer
      let height = Buffer.length1 buffer

      if height > 0 then
        for x = 0 to width - 1 do
          buffer[0, x] <- line Horizontal
          buffer[height - 1, x] <- line Horizontal

      if width > 0 then
        for y = 0 to height - 1 do
          buffer[y, 0] <- line Vertical
          buffer[y, width - 1] <- line Vertical

      if width > 0 && height > 0 then
        buffer[0, 0] <- line TopLeft
        buffer[0, width - 1] <- line TopRight
        buffer[height - 1, 0] <- line BottomLeft
        buffer[height - 1, width - 1] <- line BottomRight

      buffer

[<AutoOpen>]
module Panel =
  let panel props (subs : (Region -> ViewTree) list) (region : Region) : ViewTree =
    let subs = subs |> List.map ^fun sub -> sub region.Inner

    ViewTree.create
      { Props = props }
      region
      subs