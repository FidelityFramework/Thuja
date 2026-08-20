namespace Thuja.Elements

open Thuja
open Thuja.View
open Thuja.Styles

type RuleProps =
  | RuleStyle of BorderStyle
  | RuleColor of Color

type internal RuleType =
  | Horizontal
  | Vertical

type internal Rule =
  { Props: RuleProps list
    Type: RuleType }
  interface IElement with
    member this.Render (region : Region): Buffer =
      // props
      let style = 
        this.Props 
        |> Seq.choose ^function | RuleStyle style -> Some style | _ -> None
        |> Seq.tryHead
        |> Option.defaultValue Normal

      let color = 
        this.Props 
        |> Seq.choose ^function | RuleColor color -> Some color | _ -> None
        |> Seq.tryHead
        |> Option.defaultValue Reset

      // lines
      let line symbol = Border.Styles.[style].[symbol].With color

      let buffer = Buffer.create region.Width region.Height
      let width = Buffer.length2 buffer
      let height = Buffer.length1 buffer

      match this.Type with
      | Horizontal ->
        if height > 0 then
          for x = 0 to width - 1 do
            buffer[0, x] <- line Border.Line.Horizontal
      | Vertical ->
        if width > 0 then
          for y = 0 to height - 1 do
            buffer[y, 0] <- line Border.Line.Vertical

      buffer

[<AutoOpen>]
module Rule =
  let hr props (region : Region) : ViewTree =
    ViewTree.create
      { Props = props; Type = Horizontal }
      region
      []

  let vr props (region : Region) : ViewTree =
    ViewTree.create
      { Props = props; Type = Vertical }
      region
      []