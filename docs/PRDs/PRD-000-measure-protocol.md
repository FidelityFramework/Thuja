# PRD-000: Measure Protocol

**Status:** Draft
**Tier:** 0 — Foundation
**Dependencies:** None (this is the root dependency)
**Unlocks:** PRD-001, PRD-011, PRD-013, PRD-020, PRD-021, and all container/layout work

---

## 1. Problem Statement

Thuja's current rendering contract is single-phase: elements receive a Region and render into it. There is no mechanism for an element to communicate its spatial needs *before* space is allocated. This means:

- Layout containers (columns, rows, grid) cannot auto-size to content
- Tables cannot compute column widths from cell content
- Panels cannot shrink-wrap their children
- Responsive behavior on terminal resize is limited to proportional fractions

In tiling window managers like Hyprland, terminal windows are aggressively resized. A TUI framework must negotiate space intelligently — asking children "how small can you go?" and "how large do you want to be?" before committing to allocation. This is the core enabler for responsive terminal layout.

## 2. Reference Analysis

### Rich (Python)

Rich defines a `Measurement` named tuple and a `__rich_measure__` protocol:

```python
class Measurement(NamedTuple):
    minimum: int   # Smallest usable width (e.g., longest word)
    maximum: int   # Ideal width (e.g., full unwrapped line)
```

Key behaviors:
- `normalize()` ensures `0 <= minimum <= maximum`
- `with_maximum(width)` clamps both values to `<= width`
- `clamp(min_width, max_width)` constrains to range
- If a renderable lacks `__rich_measure__`, Rich assumes `(0, available_width)`

Container aggregation: `measure_renderables()` takes the widest minimum and widest maximum across children.

**How Table uses it:** Measures every cell in each column, gets per-column (min, max). If total maximums exceed available width, collapses wrappable columns proportionally. If under budget with `expand=True`, distributes surplus by ratio.

**How Text measures:** maximum = longest line cell count. minimum = longest *word* cell count (the word-wrap breakpoint). This is what enables responsive text reflow.

### Spectre.Console (C#)

Folds Measure into `IRenderable` directly:
```csharp
Measurement Measure(RenderOptions options, int maxWidth);
```
Same (min, max) concept but as a method on the renderable interface rather than a separate protocol.

### Thuja (Current)

No measurement. `IElement` has only `Render(region: Region) -> Command list`. Layout.fs hardcodes `Fraction`/`Absolute` sizing without consulting children.

## 3. F# Native Design

### Core Type

```fsharp
/// The spatial measurement of an element: how much width it needs.
/// Minimum is the smallest usable width (e.g., longest word in wrappable text).
/// Maximum is the ideal width (e.g., full content without wrapping).
[<Struct>]
type Measurement = {
    Minimum: int
    Maximum: int
}

module Measurement =
    /// A zero-size measurement (empty element).
    let zero = { Minimum = 0; Maximum = 0 }

    /// Normalize ensures 0 <= Minimum <= Maximum.
    let normalize (m: Measurement) =
        let min = max 0 m.Minimum
        let max' = max min m.Maximum
        { Minimum = min; Maximum = max' }

    /// Clamp both values to not exceed the given width.
    let clampMax (width: int) (m: Measurement) =
        { Minimum = min m.Minimum width; Maximum = min m.Maximum width }
        |> normalize

    /// Clamp both values to be at least the given width.
    let clampMin (width: int) (m: Measurement) =
        { Minimum = max m.Minimum width; Maximum = max m.Maximum width }

    /// Clamp to a range.
    let clamp (lo: int) (hi: int) (m: Measurement) =
        m |> clampMin lo |> clampMax hi

    /// Combine measurements from siblings: widest minimum, widest maximum.
    let combine (measurements: Measurement list) =
        match measurements with
        | [] -> zero
        | ms ->
            { Minimum = ms |> List.map _.Minimum |> List.max
              Maximum = ms |> List.map _.Maximum |> List.max }

    /// Sum measurements (for horizontal stacking).
    let sum (measurements: Measurement list) =
        match measurements with
        | [] -> zero
        | ms ->
            { Minimum = ms |> List.sumBy _.Minimum
              Maximum = ms |> List.sumBy _.Maximum }
```

### Integration with IElement

The measure protocol is added as a separate interface rather than modifying IElement, following Rich's approach of keeping measurement optional:

```fsharp
/// Elements that can report their spatial needs implement this.
type IMeasurable =
    /// Measure the element's width requirements given a maximum available width.
    /// The maxWidth parameter allows elements to adapt their measurement
    /// (e.g., text can report a smaller minimum if forced into a narrow space).
    abstract Measure: maxWidth: int -> Measurement
```

Elements that implement `IMeasurable` participate in intelligent layout. Those that don't are treated as `{ Minimum = 0; Maximum = availableWidth }` — they fill whatever space they're given (same default as Rich).

### Why a Separate Interface (Not Part of IElement)

1. **Backward compatibility:** Existing elements continue to work unchanged
2. **Opt-in complexity:** Simple elements (Empty, Rule, Raw) don't need measurement
3. **Follows Rich's model:** Separate protocol, checked at runtime
4. **Cleaner F# idiom:** Pattern match on capability rather than forcing all elements to implement a method that returns a meaningless default

### Measurement Context

Elements may need context beyond `maxWidth` to measure correctly (e.g., terminal capabilities for wide character handling). This is provided through an options record:

```fsharp
/// Context provided to measurement, carrying constraints and capabilities.
type MeasureContext = {
    MaxWidth: int
    MaxHeight: int
}
```

This avoids coupling to any runtime-specific type. The context is pure data — no System.Console, no IBackend, no thread-local state.

## 4. Integration with Thuja Core

### Layout.fs Changes

The layout system becomes measurement-aware. The current `LayoutProps` type gains a new case:

```fsharp
type LayoutProps =
    | Absolute of size: int      // Fixed size (unchanged)
    | Fraction of value: int     // Proportional share (unchanged)
    | Auto                       // NEW: size to content via Measure
```

The `Auto` case triggers the measure phase:

```fsharp
// Pseudocode for measurement-aware layout allocation
let allocate (props: LayoutProps list) (children: IElement list) (available: int) =
    // Phase 1: Measure Auto children
    let measurements =
        List.zip props children
        |> List.map (fun (prop, child) ->
            match prop with
            | Absolute size -> (size, size)   // fixed: min = max = size
            | Auto ->
                match child with
                | :? IMeasurable as m ->
                    let meas = m.Measure(available)
                    (meas.Minimum, meas.Maximum)
                | _ -> (0, available)          // no measure = fill
            | Fraction _ -> (0, available))    // fractions get remainder

    // Phase 2: Allocate fixed and auto-minimum first
    let fixedTotal = ... // sum of Absolute sizes
    let autoMinTotal = ... // sum of Auto minimum widths
    let remaining = available - fixedTotal - autoMinTotal

    // Phase 3: Distribute remaining to Fractions proportionally
    // Phase 4: If surplus, expand Auto children toward their maximums
    // Phase 5: If deficit, collapse Auto children toward their minimums
    ...
```

### ViewTree Compatibility

Measurement does not change the ViewTree structure. It happens *during* view construction (when Layout elements allocate child regions), before the tree is built. The diffing engine continues to compare rendered ViewTrees — measurement is a layout-time concern, not a render-time concern.

### Program.fs Integration

No changes to the Elm loop. Measurement happens inside the `view` function's layout calls. The loop continues to:
1. Detect terminal size change
2. Call `view model region` (measurement happens inside layout elements here)
3. Diff the resulting ViewTree
4. Render changes

## 5. Responsive Behavior

### The Resize Scenario

Terminal shrinks from 120 to 60 columns (Hyprland splits a window):

1. Backend reports new TerminalSize (60, H)
2. Root Region is (0, 0, 59, H-1)
3. Layout calls Measure on children with maxWidth=60
4. Text element: minimum=12 (longest word), maximum=80 (full line)
5. Table: minimum=30 (sum of column minimums), maximum=100
6. Panel: minimum=child.min+2 (borders), maximum=child.max+2
7. Layout allocates: if total minimums <= 60, distribute surplus proportionally
8. If total minimums > 60, elements at minimums — content wraps, columns compress

### Collapse Behavior

When available width < sum of minimums, layout must decide what to sacrifice. Strategy (following Rich's model):

1. Non-wrappable elements (Absolute size) keep their size
2. Wrappable elements (text, tables) shrink proportionally toward zero
3. Elements at width < their minimum render with overflow (clip/ellipsis)

### Responsive Breakpoints

Elements can express discrete layout preferences:

```fsharp
/// A measurement that changes behavior at breakpoints.
/// Example: A sidebar might be 30 wide normally, collapse to icon-width (4)
/// below 80 total, and disappear below 40 total.
type LayoutProps =
    // ... existing cases ...
    | Responsive of breakpoints: (int * LayoutProps) list
    // (threshold, prop): use `prop` when available width >= threshold
    // Falls through to smallest matching threshold
```

This is not in Rich or Spectre.Console — it's a natural extension for the tiling WM use case where terminals regularly cross width thresholds.

## 6. API Surface

### For Element Authors

```fsharp
// A text element that measures itself
type Text = { Content: string; Props: TextProps list }
    interface IElement with ...
    interface IMeasurable with
        member this.Measure(maxWidth) =
            let lines = this.Content.Split('\n')
            let longestWord = lines |> ... |> max
            let longestLine = lines |> ... |> max
            { Minimum = min longestWord maxWidth
              Maximum = min longestLine maxWidth }
```

### For Layout Users

```fsharp
// Auto-sized columns based on content
columns [ Auto; Fraction 1; Absolute 20 ] [
    panel [] [ text [] "Short" ]      // Auto: shrink-wraps to content
    text [] longContent               // Fraction: gets remaining space
    statusBar                         // Absolute: always 20 wide
]
```

## 7. .NET-Free Design Notes

This PRD is fully .NET-free by design:

- `Measurement` is a pure F# struct record — no BCL types
- `IMeasurable` uses F# abstract members — maps to any runtime's vtable/dispatch
- `MeasureContext` is a plain record — no System.Console, no IServiceProvider
- All functions are pure (input -> output) with no side effects
- No threading, no async, no IO — measurement is synchronous and deterministic
- The only F# language features used are: records, DUs, interfaces, modules, functions

When Firefly replaces the .NET runtime, these types compile unchanged. The measurement phase is pure computation over immutable data.

## 8. Acceptance Criteria

- [ ] `Measurement` type defined in Core.fs or new Measure.fs
- [ ] `IMeasurable` interface defined alongside IElement
- [ ] `Measurement` module with `zero`, `normalize`, `clampMax`, `clampMin`, `combine`, `sum`
- [ ] `Text` element implements `IMeasurable` (min = longest word, max = longest line)
- [ ] `Panel` element implements `IMeasurable` (child measurement + border overhead)
- [ ] `Table` element implements `IMeasurable` (column measurement aggregation)
- [ ] `Layout.columns`/`rows` support `Auto` sizing via measurement
- [ ] Existing `Fraction`/`Absolute` behavior unchanged (backward compatible)
- [ ] Sample app demonstrating responsive resize behavior
- [ ] All measurement code is pure functions with no BCL dependencies
- [ ] No `System.*` imports in measurement-related code
