module internal Thuja.Backend.Helpers

open Thuja
open Thuja.Backend.Mappings

open Tutu.Events 
open Tutu.Commands
open Tutu.Terminal
open Tutu.Extensions

open System.Threading

let debounce (timeout : int) (action: 'args -> unit) =
  let mutable cts : CancellationTokenSource option = None
  
  fun args ->
    cts |> Option.iter (fun c -> c.Cancel(); c.Dispose())
    
    cts <- Some <| new CancellationTokenSource()
    
    Async.Start (async {
        do! Async.Sleep timeout
        if not cts.Value.IsCancellationRequested then
            action args
    }, cts.Value.Token)

let (|KeyCodeEvent|_|) (event : IEvent) =
  match event with
  | :? Event.KeyEventEvent as event when event.Event.Kind = KeyEventKind.Press ->
  
      let key : KeyInput option = Mapper.map event.Event.Code
      let modifiers : Thuja.KeyModifiers option = Mapper.map event.Event.Modifiers

      (key, modifiers)
      ||> Option.map2 (fun key modifiers -> KeyCodeEvent (key, modifiers)) 

  | _ -> None

let (|TerminalResizeEvent|_|) (event : IEvent) =
  match event with
  | :? Event.ScreenResizeEvent as event -> Some (event.Column, event.Row)
  | _ -> None

module Terminal =
  let execute commands =
    commands
    |> Seq.toArray
    |> System.Console.Out.Enqueue
    |> _.Flush()
    |> ignore

  let beginSession () =
    SystemTerminal.Instance.EnableRawMode()
    execute [ 
      Terminal.EnterAlternateScreen
      Events.EnableMouseCapture
      Cursor.Hide
    ]

  let endSession () =
    execute [
      Cursor.Show
      Events.DisableMouseCapture
      Terminal.LeaveAlternateScreen 
    ]
    SystemTerminal.Instance.DisableRawMode()