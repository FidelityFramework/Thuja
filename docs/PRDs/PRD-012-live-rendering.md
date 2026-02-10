# PRD-012: Live Rendering and Spinner System

**Status:** Draft
**Tier:** 1 — Core Capability
**Dependencies:** PRD-001 (Segment Model), PRD-002 (Color System)
**Unlocks:** PRD-013 (Progress System), animated status displays, real-time dashboards

---

## 1. Problem Statement

Thuja's Elm loop re-renders on message receipt — it's event-driven. There's no built-in mechanism for time-based animation (spinners, pulsing progress bars, blinking cursors, live-updating clocks) without the user manually creating subscriptions and managing frame timing.

Rich's most visible feature is its animated output — the spinning dots during AI inference, the filling progress bars, the live-updating status displays. These create the perception of a responsive, polished application. This capability must be native to the framework, not bolted on by each user.

## 2. Reference Analysis

### Rich (Python) — Live

- Context manager `Live(renderable)` that auto-refreshes at configurable rate
- Render hook intercepts output, repositions cursor to overwrite previous render
- Daemon thread drives refresh cycle
- NOT diff-based: full redraw via cursor-up + erase-line per previous row
- Vertical overflow handling: crop, ellipsis, visible

### Rich (Python) — Status

- Wraps Live with a spinner + message display
- ~90 built-in spinner styles (Dots, Line, Star, BouncingBar, etc.)
- Spinner is an iterable of frames (list of strings) + interval
- Status text updated via `ctx.update("new message")`

### Thuja (Current)

- Subscriptions exist: `Sub<'msg> = ('msg -> unit) -> unit`
- Users can create timer subscriptions manually
- No built-in frame-rate management or animation primitives
- ViewTree diffing handles efficient re-rendering of changes

### Key Insight

Thuja's existing architecture is actually *better positioned* for live rendering than Rich. Rich does full redraws because it has no diff engine. Thuja has ViewTree diffing, so animated elements only need to signal that they've changed — the diff engine handles minimal re-rendering automatically.

The missing piece is a framework-level subscription that drives a tick clock, and element types that advance their state on tick.

## 3. F# Native Design

### Animation Clock

```fsharp
/// A tick message for animation.
type Tick = Tick of frameIndex: int64 * elapsed: int64  // frame count, elapsed ms

/// Create a subscription that emits ticks at a given interval.
/// The tick function is injected to avoid System.Threading dependency.
/// In .NET mode, this uses Async.Sleep. In Firefly, it uses the native scheduler.
let tickSubscription
    (intervalMs: int)
    (toMsg: Tick -> 'msg)
    : Sub<'msg> =
    fun dispatch ->
        // Implementation provided by runtime adapter (see .NET-Free section)
        ()
```

The tick subscription is the *only* piece that touches concurrency. Everything else is pure.

### Spinner Type

```fsharp
/// A spinner is a cycling sequence of frames.
type Spinner = {
    Frames: string list
    IntervalMs: int
}

module Spinner =
    /// Advance the spinner to the frame for the given elapsed time.
    let frame (elapsed: int64) (spinner: Spinner) : string =
        let frameIndex = (elapsed / int64 spinner.IntervalMs) % int64 (List.length spinner.Frames)
        spinner.Frames.[int frameIndex]

    // --- Built-in spinner styles ---
    let dots = { Frames = ["⠋";"⠙";"⠹";"⠸";"⠼";"⠴";"⠦";"⠧";"⠇";"⠏"]; IntervalMs = 80 }
    let dots2 = { Frames = ["⣾";"⣽";"⣻";"⢿";"⡿";"⣟";"⣯";"⣷"]; IntervalMs = 80 }
    let line = { Frames = ["-";"\\";"|";"/"]; IntervalMs = 130 }
    let star = { Frames = ["✶";"✸";"✹";"✺";"✹";"✷"]; IntervalMs = 70 }
    let bouncingBar = { Frames = ["[    ]";"[=   ]";"[==  ]";"[=== ]";"[ ===]";"[  ==]";"[   =]";"[    ]"]; IntervalMs = 80 }
    let arc = { Frames = ["◜";"◠";"◝";"◞";"◡";"◟"]; IntervalMs = 100 }
    let clock = { Frames = ["🕛";"🕐";"🕑";"🕒";"🕓";"🕔";"🕕";"🕖";"🕗";"🕘";"🕙";"🕚"]; IntervalMs = 100 }
    let earth = { Frames = ["🌍";"🌎";"🌏"]; IntervalMs = 180 }
    let moon = { Frames = ["🌑";"🌒";"🌓";"🌔";"🌕";"🌖";"🌗";"🌘"]; IntervalMs = 80 }
    let runner = { Frames = ["🚶";"🏃"]; IntervalMs = 140 }

    // Additional styles to match Rich's ~90 spinners
    // (full catalog defined in data, not hard-coded functions)
    let all : Map<string, Spinner> = ...
```

### Status Element

```fsharp
/// A status display combining spinner + message.
/// Pure function: given elapsed time, produces the current frame.
type StatusView = {
    Spinner: Spinner
    Message: string
    Style: Style
}

module StatusView =
    /// Render the status for a given elapsed time.
    let render (elapsed: int64) (status: StatusView) : StyledText =
        let frame = Spinner.frame elapsed status.Spinner
        [ { Text = frame + " "; Style = status.Style }
          { Text = status.Message; Style = status.Style } ]
```

### Integration with Elm Architecture

Live rendering fits naturally into MVU:

```fsharp
// Model carries elapsed time for animations
type Model = {
    Status: string
    Elapsed: int64
    // ... other state
}

type Msg =
    | AnimationTick of Tick
    | DataLoaded of Data
    // ...

// Update: advance animation state
let update model = function
    | AnimationTick (Tick (_, elapsed)) ->
        { model with Elapsed = elapsed }, Cmd.none
    | ...

// View: pure function uses elapsed to compute animation frame
let view model =
    columns [ LayoutSlot.auto; LayoutSlot.fr 1 ] [
        fun region ->
            let frame = Spinner.frame model.Elapsed Spinner.dots
            text [ Color (Named Cyan) ] frame |> renderInRegion region
        fun region ->
            text [] model.Status |> renderInRegion region
    ]

// Program: subscribe to animation ticks
Program.make model view update
|> Program.withSubscriptions [ tickSubscription 80 AnimationTick ]
|> Program.withTutuBackend
|> Program.run
```

### Convenience: `status` Element

```fsharp
/// A high-level status element that manages its own animation frame.
let status (spinner: Spinner) (elapsed: int64) (message: string) : Region -> ViewTree =
    fun region ->
        let frame = Spinner.frame elapsed spinner
        let content = frame + " " + message
        text [ Color (Named Cyan) ] content |> renderInRegion region
```

## 4. Integration with Thuja Core

### Tick Subscription in Program.fs

The tick subscription plugs into the existing subscription system:

```fsharp
Program.make model view update
|> Program.withSubscriptions [
    tickSubscription 80 AnimationTick  // 80ms = ~12.5 fps, smooth for spinners
]
```

When a tick arrives:
1. MailboxProcessor receives AnimationTick message
2. Update advances elapsed time in model
3. View is called → animated elements compute new frames
4. ViewTree diff detects changed spinner text
5. Only the spinner region is re-rendered

This is dramatically more efficient than Rich's full-redraw — only the few characters of the spinner are written to the terminal on each tick.

### Frame Rate Management

Different animations need different frame rates. The framework supports multiple subscriptions at different intervals:

```fsharp
|> Program.withSubscriptions [
    tickSubscription 80 SpinnerTick     // Fast: spinners
    tickSubscription 1000 ClockTick     // Slow: clock display
    tickSubscription 250 ProgressTick   // Medium: progress bars
]
```

Or a single tick with frame skipping per element (simpler, one subscription):

```fsharp
|> Program.withSubscriptions [
    tickSubscription 50 AnimationTick   // 20 fps master clock
]
// Elements internally skip frames based on their interval
```

## 5. Responsive Behavior

Animation continues seamlessly through terminal resize:
1. Resize triggers view rebuild with new Region
2. Animated elements render at their current frame in the new layout
3. ViewTree diff handles the transition
4. Next tick continues animation without interruption

The spinner frame is derived from elapsed time, not frame count, so resizes don't cause animation stuttering.

## 6. API Surface

### Minimal (Status Display)

```fsharp
// In your model
type Model = { Status: string; Elapsed: int64 }
type Msg = Tick of Tick | ...

// In your view
status Spinner.dots model.Elapsed model.Status

// In your program
Program.make model view update
|> Program.withSubscriptions [ tickSubscription 80 Tick ]
```

### Custom Animation

```fsharp
// Any element can be animated by deriving its view from elapsed time
let pulsingBorder elapsed =
    let brightness = abs (sin (float elapsed / 500.0))
    let color = Rgb (byte (brightness * 255.0), 0uy, 0uy)
    panel [ BorderColor color ] [ text [] "Alert!" ]
```

### Spinner Catalog

```fsharp
Spinner.dots        // ⠋ ⠙ ⠹ ⠸ ⠼ ⠴ ⠦ ⠧ ⠇ ⠏
Spinner.line        // - \ | /
Spinner.star        // ✶ ✸ ✹ ✺ ✹ ✷
Spinner.arc         // ◜ ◠ ◝ ◞ ◡ ◟
Spinner.bouncingBar // [=   ] [==  ] ...
Spinner.clock       // 🕛 🕐 🕑 ...
Spinner.earth       // 🌍 🌎 🌏
Spinner.moon        // 🌑 🌒 🌓 ...
```

## 7. .NET-Free Design Notes

The core animation system is entirely pure:

- `Spinner` is a record of `string list * int` — no runtime dependency
- `Spinner.frame` is pure: `int64 -> Spinner -> string`
- `StatusView.render` is pure: `int64 -> StatusView -> StyledText`
- All frame computation is deterministic arithmetic

The *only* impure piece is `tickSubscription`, which needs a timer. This is abstracted:

```fsharp
/// Timer abstraction — injected by the runtime.
type ITimer =
    abstract StartInterval: intervalMs: int * callback: (unit -> unit) -> unit
    abstract Stop: unit -> unit

/// The tick subscription is parameterized by the timer implementation.
let makeTickSubscription (timer: ITimer) (intervalMs: int) (toMsg: Tick -> 'msg) : Sub<'msg> =
    fun dispatch ->
        let mutable frame = 0L
        let mutable elapsed = 0L
        timer.StartInterval(intervalMs, fun () ->
            frame <- frame + 1L
            elapsed <- elapsed + int64 intervalMs
            dispatch (toMsg (Tick (frame, elapsed))))
```

On .NET: `ITimer` is implemented via `System.Threading.Timer` or `Async.Sleep`. On Firefly: `ITimer` is implemented via the native scheduler. The animation logic doesn't know or care which runtime drives the ticks.

## 8. Acceptance Criteria

- [ ] `Spinner` type defined with `Frames` and `IntervalMs`
- [ ] `Spinner.frame` computes correct frame from elapsed time
- [ ] At least 20 built-in spinner styles (matching Rich's popular ones)
- [ ] `tickSubscription` integrates with Elm subscription system
- [ ] `status` convenience element renders spinner + message
- [ ] Animation is smooth at 80ms intervals (~12.5 fps)
- [ ] ViewTree diff correctly detects spinner frame changes (minimal re-render)
- [ ] Terminal resize doesn't interrupt animation
- [ ] Timer abstraction allows runtime injection (no hardcoded System.Threading)
- [ ] Multiple tick rates supported (different subscriptions or frame skipping)
- [ ] Elapsed-time-based frames (not frame-count) for consistent animation speed
- [ ] Sample app demonstrating spinner + status display
- [ ] No `System.Threading` imports in core animation code
