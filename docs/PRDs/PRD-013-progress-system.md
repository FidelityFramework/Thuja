# PRD-013: Progress System

**Status:** Draft
**Tier:** 1 — Core Capability
**Dependencies:** PRD-000 (Measure), PRD-001 (Segment), PRD-010 (Markup), PRD-012 (Live Rendering)
**Unlocks:** Polished CLI tools, AI inference displays, batch processing UIs

---

## 1. Problem Statement

Progress bars are the signature widget of Rich and the primary visual element in AI terminal applications (model downloads, inference streaming, data processing). Thuja has no progress bar primitive. Building one requires:

- A composable column system (description, bar, percentage, speed, ETA, spinner)
- Smooth animation via the live rendering system
- Multiple concurrent tasks with independent progress
- Determinate (known total) and indeterminate (pulsing) modes
- Responsive resizing — the bar fills available space, other columns are fixed-width

## 2. Reference Analysis

### Rich (Python)

Rich's progress system is its most sophisticated widget:

**Architecture:**
```
Progress (context manager)
  → Live (handles display refresh)
    → Table.grid (layout container)
      → ProgressColumn subclasses (each renders one aspect)
```

**Composable columns:**
- `TextColumn` — task description
- `BarColumn` — the actual progress bar (filled/empty blocks)
- `SpinnerColumn` — animated spinner
- `PercentageColumn` — "42.0%"
- `TimeElapsedColumn` — "0:01:23"
- `TimeRemainingColumn` — "0:00:45" (estimated)
- `DownloadColumn` — "1.2/5.0 GB"
- `TransferSpeedColumn` — "2.3 MB/s"

**Task model:**
- `task.completed` / `task.total` for progress ratio
- Speed calculated from a moving window of samples
- `task.description` and `task.fields` for custom display
- Tasks can be visible, started, finished, or invisible

**Bar rendering:**
- Full block: `━` (completed), Half: partial fill character
- Pulse mode: animated gradient for indeterminate tasks
- Finished style distinct from in-progress

### Spectre.Console (C#)

Same composable column model. Additionally supports `MaxWidth` on columns, caching per column to reduce re-render frequency.

### Thuja (Current)

No progress widget. Users would need to manually compose Text + Raw elements and manage state in the Elm model.

## 3. F# Native Design

### Task Model

```fsharp
/// A tracked task with progress state.
type ProgressTask = {
    Id: int
    Description: string
    Total: float option         // None = indeterminate
    Completed: float
    Visible: bool
    StartTime: int64            // Elapsed ms at start (from animation clock)
    Samples: (int64 * float) list  // (elapsed, completed) pairs for speed calc
}

module ProgressTask =
    /// Progress ratio (0.0 to 1.0). None if indeterminate.
    let ratio (task: ProgressTask) : float option =
        task.Total |> Option.map (fun total ->
            if total <= 0.0 then 1.0
            else min 1.0 (task.Completed / total))

    /// Whether the task is complete.
    let isFinished (task: ProgressTask) : bool =
        task.Total |> Option.map (fun t -> task.Completed >= t) |> Option.defaultValue false

    /// Calculate speed (units per second) from recent samples.
    let speed (task: ProgressTask) : float option =
        match task.Samples with
        | [] | [_] -> None
        | samples ->
            let recent = samples |> List.take (min 10 (List.length samples))
            let (t1, c1) = List.last recent
            let (t0, c0) = List.head recent
            let dt = float (t1 - t0) / 1000.0
            if dt > 0.0 then Some ((c1 - c0) / dt) else None

    /// Estimated time remaining in seconds.
    let eta (task: ProgressTask) : float option =
        match ratio task, speed task with
        | Some r, Some s when s > 0.0 && r < 1.0 ->
            task.Total |> Option.map (fun total ->
                (total - task.Completed) / s)
        | _ -> None
```

### Progress Columns

Each column is a pure rendering function:

```fsharp
/// A progress column renders one aspect of a task.
type ProgressColumn = {
    /// Render this column for a task at the given elapsed time.
    Render: ProgressTask -> int64 -> int -> StyledText  // task, elapsed, width -> styled output
    /// Measurement: how wide this column wants to be.
    Measure: Measurement
}

module ProgressColumns =
    /// Task description text.
    let description (style: Style) : ProgressColumn =
        { Render = fun task _ width ->
            [ { Text = String.truncateWithEllipsis width task.Description; Style = style } ]
          Measure = { Minimum = 8; Maximum = 40 } }

    /// The progress bar itself.
    let bar (completedStyle: Style) (remainingStyle: Style) : ProgressColumn =
        { Render = fun task elapsed width ->
            match ProgressTask.ratio task with
            | Some ratio ->
                let filled = int (float width * ratio)
                let empty = width - filled
                [ { Text = String.replicate filled "━"; Style = completedStyle }
                  { Text = String.replicate empty "─"; Style = remainingStyle } ]
            | None ->
                // Indeterminate: pulsing animation
                let pulse = pulsePattern elapsed width
                pulse
          Measure = { Minimum = 10; Maximum = 1000 } }  // bar fills remaining space

    /// Percentage display: "42.0%"
    let percentage (style: Style) : ProgressColumn =
        { Render = fun task _ _ ->
            let pct = ProgressTask.ratio task |> Option.defaultValue 0.0
            [ { Text = $"{pct * 100.0:F1}%%"; Style = style } ]
          Measure = { Minimum = 6; Maximum = 6 } }  // fixed width

    /// Spinner column.
    let spinner (spinner: Spinner) (style: Style) : ProgressColumn =
        { Render = fun _ elapsed _ ->
            [ { Text = Spinner.frame elapsed spinner; Style = style } ]
          Measure = { Minimum = 2; Maximum = 2 } }

    /// Elapsed time: "0:01:23"
    let elapsed (style: Style) : ProgressColumn =
        { Render = fun task elapsed _ ->
            let secs = (elapsed - task.StartTime) / 1000L
            let h = secs / 3600L
            let m = (secs % 3600L) / 60L
            let s = secs % 60L
            [ { Text = $"{h}:{m:D2}:{s:D2}"; Style = style } ]
          Measure = { Minimum = 7; Maximum = 7 } }

    /// Estimated remaining: "0:00:45"
    let remaining (style: Style) : ProgressColumn =
        { Render = fun task _ _ ->
            match ProgressTask.eta task with
            | Some secs ->
                let s = int64 secs
                [ { Text = $"{s / 3600L}:{(s % 3600L) / 60L:D2}:{s % 60L:D2}"; Style = style } ]
            | None ->
                [ { Text = "-:--:--"; Style = style } ]
          Measure = { Minimum = 7; Maximum = 7 } }

    /// Transfer speed: "2.3 MB/s"
    let speed (unitName: string) (style: Style) : ProgressColumn =
        { Render = fun task _ _ ->
            match ProgressTask.speed task with
            | Some s -> [ { Text = $"{formatSize s}/{unitName}"; Style = style } ]
            | None -> [ { Text = $"?/{unitName}"; Style = style } ]
          Measure = { Minimum = 10; Maximum = 10 } }
```

### Progress View

```fsharp
/// Render a progress display for multiple tasks.
let progressView
    (columns: ProgressColumn list)
    (tasks: ProgressTask list)
    (elapsed: int64)
    : Region -> ViewTree =
    fun region ->
        // Allocate column widths using Measure protocol
        let measurements = columns |> List.map _.Measure
        let widths = allocateColumns region.Width measurements
        // Render each visible task as a row
        let visibleTasks = tasks |> List.filter _.Visible
        rows (visibleTasks |> List.map (fun _ -> LayoutSlot.fixed 1)) [
            for task in visibleTasks do
                fun rowRegion ->
                    columns
                    |> List.map2 (fun width col ->
                        col.Render task elapsed width
                    ) widths
                    |> renderAsColumns rowRegion widths
        ] region
```

## 4. Integration with Thuja Core

### In the Elm Model

```fsharp
type Model = {
    Tasks: ProgressTask list
    Elapsed: int64
}

type Msg =
    | Tick of Tick
    | AddTask of string * float option  // description, total
    | Advance of taskId: int * amount: float
    | TaskDone of taskId: int

let update model = function
    | Tick (Tick (_, elapsed)) ->
        { model with Elapsed = elapsed }, Cmd.none
    | AddTask (desc, total) ->
        let task = { Id = nextId(); Description = desc; Total = total; ... }
        { model with Tasks = task :: model.Tasks }, Cmd.none
    | Advance (id, amount) ->
        let tasks = model.Tasks |> List.map (fun t ->
            if t.Id = id then
                { t with Completed = t.Completed + amount
                         Samples = (model.Elapsed, t.Completed + amount) :: t.Samples |> List.truncate 10 }
            else t)
        { model with Tasks = tasks }, Cmd.none
```

### In the View

```fsharp
let view model =
    progressView
        [ ProgressColumns.spinner Spinner.dots defaultStyle
          ProgressColumns.description defaultStyle
          ProgressColumns.bar completedStyle remainingStyle
          ProgressColumns.percentage dimStyle
          ProgressColumns.remaining dimStyle ]
        model.Tasks
        model.Elapsed
```

## 5. Responsive Behavior

- The `bar` column has `Maximum = 1000` (effectively unbounded) — it absorbs all remaining space after fixed-width columns are allocated
- On narrow terminals, the bar shrinks toward its minimum (10)
- If extremely narrow, fixed columns (percentage, time) can be hidden via `HiddenBelow` visibility on their layout slots
- Task descriptions truncate with ellipsis

## 6. API Surface

### Quick Progress

```fsharp
// Simplest usage: default columns, single task
let progressColumns = ProgressColumns.defaults  // spinner, desc, bar, %, eta

progressView progressColumns model.Tasks model.Elapsed
```

### Custom Progress

```fsharp
// AI inference style: spinner, description, bar, speed
progressView [
    ProgressColumns.spinner Spinner.dots2 (Style.fg (Named Cyan))
    ProgressColumns.description (Style.fg (Named White))
    ProgressColumns.bar
        (Style.fg (Named Green))
        (Style.fg (Named BrightBlack))
    ProgressColumns.speed "tok" (Style.fg (Named BrightBlack))
] model.Tasks model.Elapsed
```

## 7. .NET-Free Design Notes

- `ProgressTask` is a plain F# record — no mutable state, no timers
- `ProgressColumn` is a record of pure functions — no side effects
- Speed calculation uses a sliding window of `(elapsed, completed)` pairs — pure math
- ETA calculation is pure arithmetic from speed and remaining amount
- The rendering functions are all `task -> elapsed -> width -> StyledText` — pure
- No `System.Diagnostics.Stopwatch`, no `DateTime` — elapsed time comes from the animation clock (PRD-012), which is injected via the Elm message system
- Time formatting is pure integer arithmetic (div, mod) — no `TimeSpan`

## 8. Acceptance Criteria

- [ ] `ProgressTask` type with completed/total/speed/eta calculations
- [ ] At least 6 built-in column types: description, bar, spinner, percentage, elapsed, remaining
- [ ] `progressView` renders multi-task, multi-column progress display
- [ ] Bar fills remaining space after fixed columns allocated
- [ ] Indeterminate tasks show pulsing animation (no known total)
- [ ] Speed calculation from moving window of samples
- [ ] ETA derived from current speed and remaining work
- [ ] Tasks can be added, advanced, hidden, and marked complete
- [ ] Responsive: bar shrinks on narrow terminals, columns can hide
- [ ] Sample app: multi-task download simulation with all column types
- [ ] No `System.Diagnostics`, `System.DateTime`, or `System.Threading` imports
- [ ] All progress logic is pure functions over immutable records
