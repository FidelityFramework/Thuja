namespace Thuja.Elements

open System

open Thuja
open Thuja.View
open Thuja.Elements
open Thuja.Styles

open Helpers

open type Border.Line

type TableProps =
  | HeadersStyle of foreground: Color * background: Color
  | RowsStyle of foreground: Color * background: Color
  | Columns of LayoutProps list
  | BorderStyle of BorderStyle
  | BorderColor of Color
  | CellStyle of row: int * column: int  * foreground: Color * background: Color

type internal Table =
  { Props: TableProps list
    Headers: string list
    Data: string list list }
  interface IElement with
    member this.Render (region : Region) : Buffer =
      // props
      let headersStyle =
        this.Props
        |> Seq.choose ^function | HeadersStyle (fg, bg) -> Some (fg, bg) | _ -> None
        |> Seq.tryHead
        |> Option.defaultValue (Color.White, Color.Reset)
        |> fun (fg, bg) -> { Style.defaultStyle with Foreground = fg; Background = bg }

      let rowsStyle =
        this.Props
        |> Seq.choose ^function | RowsStyle (fg, bg) -> Some (fg, bg) | _ -> None
        |> Seq.tryHead
        |> Option.defaultValue (Color.DarkGrey, Color.Reset)
        |> fun (fg, bg) -> { Style.defaultStyle with Foreground = fg; Background = bg }

      let cellsStyles = 
        this.Props 
        |> Seq.choose ^function 
            | CellStyle (row, column, fg, bg) -> Some ((row, column), { Style.defaultStyle with Foreground = fg; Background = bg }) 
            | _ -> None
        |> Map.ofSeq

      let borderStyle =
        this.Props
        |> Seq.choose ^function | BorderStyle style -> Some style | _ -> None
        |> Seq.tryHead
        |> Option.defaultValue Normal

      let borderColor =
        this.Props
        |> Seq.choose ^function | BorderColor color -> Some color | _ -> None
        |> Seq.tryHead
        |> Option.defaultValue Reset

      let isBordered =
        this.Props
        |> Seq.exists ^function
          | BorderStyle _ -> true
          | BorderColor _ -> true
          | _ -> false

      let columnsProps =
        this.Props
        |> Seq.choose ^function | Columns layout -> Some layout | _ -> None
        |> Seq.tryHead
        |> Option.defaultValue (List.replicate this.Headers.Length <| Fraction 1)

      let gap = if isBordered then 2 else 0

      // layout 
      let fractions = 
        columnsProps
        |> Seq.choose ^function | Fraction value -> Some value | _ -> None
      
      let fixedSize =
        columnsProps
        |> Seq.choose ^function | Absolute size -> Some (size + gap + 1) | _ -> None
        |> Seq.sum

      // calculations
      let total = Seq.sum fractions
      let width = region.Width - fixedSize - 1

      let calculateSize = function
        | Fraction fraction -> width * fraction/total
        | Absolute size -> size + gap + 1

      let columnsPositions =
        columnsProps
        |> Seq.scan (fun left prop -> left + calculateSize prop) 0
        |> Seq.toList

      let columnsWidths =
        columnsPositions
        |> Seq.pairwise
        |> Seq.map ^fun (l, r) -> Math.Max(r - l - gap - 1, 0)
        |> Seq.toList

      let buffer = Buffer.create region.Width region.Height

      // headers
      for columnIndex, cell in Seq.indexed this.Headers do
        let width = columnsWidths[columnIndex]
        let text = String.truncate true width cell
        let x, y = columnsPositions[columnIndex] + gap, Math.Max(0, gap - 1)
        Buffer.writeText buffer headersStyle text x y

      // rows
      for rowIndex, row in Seq.indexed this.Data do
        for columnIndex, cell in Seq.indexed row do
        let width = columnsWidths[columnIndex]
        let text = String.truncate true width cell
        let style = cellsStyles |> Map.tryFind (rowIndex, columnIndex) |> Option.defaultValue rowsStyle
        let x, y = columnsPositions[columnIndex] + gap, rowIndex + gap + 1 // headers gap
        Buffer.writeText buffer style text x y

      let width = Math.Min(region.Width, Seq.last columnsPositions + 1)
      let height = Math.Min(region.Height, Seq.length this.Data + gap + 2) // content + headers + borders

      // borders
      if isBordered then
        let border symbol = Border.Styles.[borderStyle].[symbol].With borderColor

        if height > 2 then
          for x = 1 to width - 2 do
            buffer[0, x] <- border Horizontal
            buffer[2, x] <- border Horizontal // separator
            buffer[height - 1, x] <- border Horizontal

        if width > 0 then
          for x in columnsPositions do
            if x < width then
              for y = 1 to height - 2 do
                buffer[y, x] <- border Vertical

        if width > 0 && height > 2 then
          buffer[0, 0] <- border TopLeft
          buffer[0, width - 1] <- border TopRight
          buffer[height - 1, 0] <- border BottomLeft
          buffer[height - 1, width - 1] <- border BottomRight

          buffer[2, 0] <- border CrossingRight // separator
          buffer[2, width - 1] <- border CrossingLeft

        if width > 0 && height > 2 && columnsPositions.Length > 2 then
          let inners = columnsPositions[1 .. columnsPositions.Length - 2]
          for x in inners do
            if x < width then
              buffer[0, x] <- border CrossingDown
              buffer[2, x] <- border Crossing
              buffer[height - 1, x] <- border CrossingUp

      buffer

[<AutoOpen>]
module Table = 
  let table (props : TableProps list) (headers : string list) (data : string list list) (region : Region) : ViewTree =
    ViewTree.create
      { Props = props; Headers = headers; Data = data }
      region 
      []