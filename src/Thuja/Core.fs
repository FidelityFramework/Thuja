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

// render
type Command =
  | MoveTo of x: int * y: int
  | Print of content: string
  | PrintWith of style: Style * content: string

// input
type KeyInput =
  | Char of char | FKey of int
  | Up | Down | Left | Right 
  | Enter | Backspace | Delete 
  | Tab | BackTab

[<Flags>]
type KeyModifiers =
  | None = 0b000 | Shift = 0b001 | Ctrl = 0b010 | Alt = 0b100

// backend
type IBackend =
  inherit IDisposable

  abstract TerminalSize : width: int * height: int with get
  abstract Execute: Command list -> unit
  abstract OnInput: IEvent<KeyInput * KeyModifiers>

// view
module View =
  type Region = 
    { X1: int; Y1: int
      X2: int; Y2: int }

  type IRenderable =
    abstract Render: Region -> Command list

  type IElement =
    inherit IRenderable
    inherit IStructuralEquatable

  type ViewTree =
    | Tree of (IElement * Region) * ViewTree list

  type Region with
    member this.Width = this.X2 - this.X1 + 1
    member this.Height = this.Y2 - this.Y1 + 1
    member this.Inner =
      { X1 = this.X1 + 1; Y1 = this.Y1 + 1
        X2 = this.X2 - 1; Y2 = this.Y2 - 1 }
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