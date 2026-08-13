namespace Thuja.Elements

module internal Helpers =
  open System
  open System.Globalization

  open Thuja
  open Thuja.View

  module Buffer =
    let writeText (buffer : Buffer) (style : Style) (text : string) (x : int) (y : int) =
      let width = Buffer.length2 buffer
      let height = Buffer.length1 buffer
      if x >= 0 && x < width && y >= 0 && y < height then
        for i = 0 to text.Length - 1 do
          if x + i < width then
            buffer[y, x + i] <- Symbol (text[i], style)

  type String with
    member this.TruncateWithEllipsis (length : int) =
      if this.Length > length
      then this[..length - 2] + "⋯"
      else this.PadRight length

    member this.Truncate (length : int) =
      if this.Length > length
      then this[..length - 1]
      else this.PadRight length

    member this.Wrap (width : int) =
      let rec loop (words: string list) position = seq {
        match words with
        | word::rest ->
            let stuff, position =
              if position > 0 then
                if position + word.Length < width then
                  " ", position + 1
                else
                  Environment.NewLine, 0
              else "", 0
            yield stuff + word
            yield! loop rest (position + word.Length)
        | _ -> ()
      }
      let words = this.Split(" ", StringSplitOptions.RemoveEmptyEntries) |> Seq.toList
      let text = loop words 0 |> String.concat ""

      text.Split Environment.NewLine

    static member truncate withEllipsis width (string : string) =
      if withEllipsis && not <| String.IsNullOrWhiteSpace string then
        string.TruncateWithEllipsis width
      else
        string.Truncate width

    static member rev (string : string) =
      StringInfo.ParseCombiningCharacters string
      |> Array.rev
      |> Seq.map ^fun ch -> StringInfo.GetNextTextElement(string, ch)
      |> String.concat ""