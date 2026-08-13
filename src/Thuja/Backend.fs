module Thuja.Backend

open System
open System.Text

open Thuja

module private ANSI =
  let [<Literal>] EnterAlternateScreen = "\x1B[?1049h"
  let [<Literal>] LeaveAlternateScreen = "\x1B[?1049l"

  let map style =
    let parameters = ResizeArray()

    if Seq.isEmpty style.Attributes then
      parameters.Add "0"
    else
      style.Attributes
      |> Seq.choose (function | SGR parameter -> Some parameter)
      |> parameters.AddRange

    match style.Foreground with
    | Reset -> parameters.Add "39"
    | Ansi code -> parameters.Add $"38;5;{code}"
    | Rgb (r, g, b) -> parameters.Add $"38;2;{r};{g};{b}"

    match style.Background with
    | Reset -> parameters.Add "49"
    | Ansi code -> parameters.Add $"48;5;{code}"
    | Rgb (r, g, b) -> parameters.Add $"48;2;{r};{g};{b}"

    if parameters.Count = 0
    then ""
    else $"\x1B[" + String.Join(";", parameters) + "m"

module private Mapper =
  let map (keyInfo : ConsoleKeyInfo) : (KeyInput * KeyModifiers) option =
    let modifiers = int32 keyInfo.Modifiers |> enum<KeyModifiers>

    let input =
      match keyInfo.Key with
      | ConsoleKey.Enter -> Some Enter
      | ConsoleKey.Backspace -> Some Backspace
      | ConsoleKey.Delete -> Some Delete
      | ConsoleKey.UpArrow -> Some Up
      | ConsoleKey.DownArrow -> Some Down
      | ConsoleKey.LeftArrow -> Some Left
      | ConsoleKey.RightArrow -> Some Right
      | ConsoleKey.Tab when modifiers.HasFlag KeyModifiers.Shift -> Some BackTab
      | ConsoleKey.Tab -> Some Tab
      | _ when keyInfo.Key >= ConsoleKey.F1 && keyInfo.Key <= ConsoleKey.F24 ->
            Some (FKey (int keyInfo.Key - int ConsoleKey.F1 + 1))
      | _ when modifiers.HasFlag KeyModifiers.Ctrl && keyInfo.Key >= ConsoleKey.A && keyInfo.Key <= ConsoleKey.Z ->
            Some (Char <| Char.ToLowerInvariant(char keyInfo.Key))
      | _ when not (Char.IsControl keyInfo.KeyChar) -> Some (Char keyInfo.KeyChar)
      | _ -> None

    if input.IsSome 
    then Some (input.Value, modifiers)
    else None

open Thuja.View
open Thuja.Styles

module private Terminal =
  let beginSession() =
    Console.OutputEncoding <- Encoding.UTF8
    Console.Write ANSI.EnterAlternateScreen
    Console.CursorVisible <- false
    Console.TreatControlCAsInput <- true
    Console.CancelKeyPress.Add(fun args -> args.Cancel <- true)

  let endSession() =
    Console.ResetColor()
    Console.CursorVisible <- true
    Console.Write ANSI.LeaveAlternateScreen

  let readKey() = Console.ReadKey true
  let size() = Console.WindowWidth - 1, Console.WindowHeight - 1

  let render (buffer : Buffer) =
    let width = Buffer.length2 buffer
    let height = Buffer.length1 buffer

    let content = StringBuilder()

    let mutable current : Style = Style.defaultStyle

    for row = 0 to height - 1 do
      for column = 0 to width - 1 do
        let ch, style = buffer[row, column]

        if style <> current then
          content.Append(ANSI.map style) |> ignore
          current <- style

        content.Append ch |> ignore

    Console.SetCursorPosition(0, 0)
    Console.Write(content.ToString())

    for (row, column), data in buffer.Raw do
      Console.SetCursorPosition(row, column)
      Console.Write data

    Console.CursorVisible <- false

type TerminalBackend() =
  do Terminal.beginSession()

  let input = new Event<KeyInput * KeyModifiers>()
  let resize = new Event<int * int>()

  do Async.Start(async {
    let mutable lastSize = Terminal.size()  
    while true do
      let size = Terminal.size()
      if size <> lastSize then
        lastSize <- size
        resize.Trigger size

      if Console.KeyAvailable then
        Terminal.readKey()
        |> Mapper.map
        |> Option.iter input.Trigger
      else
        do! Async.Sleep 33
  })

  interface IBackend with
    member _.TerminalSize : int * int =
      Terminal.size()

    member _.Render(buffer : Buffer) : unit =
      let width, height = Terminal.size()
    
      if width > 0 && height > 0 then 
        Terminal.render buffer

    member _.OnInput : IEvent<KeyInput * KeyModifiers> =
      input.Publish

    member _.OnResize : IEvent<int * int> =
      resize.Publish

    member _.Dispose() : unit =
      Terminal.endSession()