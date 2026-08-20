namespace Thuja

open System 
open System.Collections

// style
type Color = 
  | Reset
  | Ansi of byte
  | Rgb of byte * byte * byte

type Attribute =
  | SGR of parameter: string

type Style = 
  { Foreground: Color
    Background: Color
    Attributes: Attribute list }

module Style =
  let defaultStyle : Style =
    { Foreground = Reset
      Background = Reset
      Attributes = [] }

// input
type KeyInput =
  | Char of char | FKey of int
  | Up | Down | Left | Right 
  | Enter | Backspace | Delete 
  | Tab | BackTab 
  | Escape | Spacebar

[<Flags>]
type KeyModifiers =
  | None = 0b000 | Alt = 0b001 | Shift = 0b010 | Ctrl = 0b100  

// view
module View =
  type Region = 
    { X1: int; Y1: int
      X2: int; Y2: int }

  type Symbol = char * Style
  type Buffer private (width, height) =
    let buffer = Array2D.create height width (Symbol (' ', Style.defaultStyle))
    let queue = ResizeArray<_>()

    member private _.Data = buffer
    member private _.Queue = queue
    member _.Item
      with get(row, column) = buffer.[row, column]
      and set(row, column) value = buffer.[row, column] <- value

    member _.EnqueueRaw (row : int, column : int) (value : string) = queue.Add((row, column), value)
    member _.Raw with get() = queue |> Seq.toList

    static member create width height = Buffer(width, height)
    static member length1 (buffer : Buffer) = Array2D.length1 buffer.Data 
    static member length2 (buffer : Buffer) = Array2D.length2 buffer.Data 
    static member blit (source : Buffer) (target : Buffer) column row = 
      let width = Buffer.length2 source
      let height = Buffer.length1 source
      Array2D.blit source.Data 0 0 target.Data column row height width

      source.Queue
      |> Seq.map ^fun ((r, c), data) -> (r + row, c + column), data 
      |> target.Queue.AddRange

  type IRenderable =
    abstract Render: Region -> Buffer

  type IElement =
    inherit IRenderable
    inherit IStructuralEquatable

  type ViewTree =
    | Tree of (IElement * Region) * ViewTree list

  type Region with
    member this.Width = this.X2 - this.X1 + 1
    member this.Height = this.Y2 - this.Y1 + 1
    member this.Inner =
      if this.Width > 1 && this.Height > 1 then
        { X1 = this.X1 + 1; Y1 = this.Y1 + 1
          X2 = this.X2 - 1; Y2 = this.Y2 - 1 }
      else Region.empty 
    static member create(x1, y1, x2, y2) : Region =
      { X1 = x1; Y1 = y1
        X2 = x2; Y2 = y2 }
    static member create(width, height) : Region =
      { X1 = 0; Y1 = 0
        X2 = width; Y2 = height }
    static member empty : Region =
      { X1 = 0; Y1 = 0
        X2 = 0; Y2 = 0 }

  module ViewTree =
    let create element region subs =
      Tree ((element, region), subs)

    let rec toSeq (Tree (value, subtrees)) = seq {
      yield value
      for tree in subtrees do
        yield! toSeq tree
    }

    let difference (aTree : ViewTree) (bTree : ViewTree) : (IElement * Region) seq =
      toSeq aTree |> Seq.filter ^fun a -> 
        toSeq bTree |> Seq.cast<IStructuralEquatable> |> Seq.forall ^fun b -> 
          not <| b.Equals(a, StructuralComparisons.StructuralEqualityComparer)

// backend
type IBackend =
  inherit IDisposable

  abstract TerminalSize : width: int * height: int with get
  abstract Render: View.Buffer -> unit
  abstract OnInput: IEvent<KeyInput * KeyModifiers>
  abstract OnResize: IEvent<int * int>