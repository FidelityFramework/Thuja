module Thuja.Tutu

open Thuja
open Thuja.Backend.Helpers
open Thuja.Backend.Mappings

open Tutu.Events 
open Tutu.Commands
open Tutu.Terminal

type TutuBackend() =
  do Terminal.beginSession()

  let input = new Event<KeyInput * Thuja.KeyModifiers>()
  let resize = new Event<int * int>()

  let debounceResize = debounce 250 resize.Trigger

  do Async.Start(async {
      while true do
          match SystemEventReader.Instance.Read() with
          | KeyCodeEvent (key, modifiers) -> input.Trigger (key, modifiers)
          | TerminalResizeEvent (column, row) -> debounceResize (column, row) 
          | _ -> ()
  })

  interface IBackend with
    member _.TerminalSize : int * int = 
      SystemTerminal.Size.Width - 1, SystemTerminal.Size.Height - 1

    member _.Execute(commands : Command list) : unit = 
      commands
      |> Seq.collect (function
          | Clear -> [ Terminal.Clear ClearType.All ]
          | MoveTo (x, y) -> [ Cursor.MoveTo(x, y) ]
          | Print content -> [ Style.Print content ]
          | PrintWith (style, content) -> [ 
              Style.PrintStyledContent <| Mapper.map (style, content)
              Style.Reset
            ])
      |> Terminal.execute
    
      Terminal.execute [ Cursor.Hide ]

    member _.OnInput : IEvent<KeyInput * Thuja.KeyModifiers> = input.Publish
    member _.OnResize : IEvent<int * int> = resize.Publish

    member _.Dispose() : unit = 
      do Terminal.endSession()

[<RequireQualifiedAccess>]
module Program =
  let withTutuBackend = Program.withBackend<TutuBackend, _, _>