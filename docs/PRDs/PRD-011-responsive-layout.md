# PRD-011: Responsive Layout System

**Status:** Draft
**Tier:** 1 — Core Capability
**Dependencies:** PRD-000 (Measure Protocol), PRD-001 (Segment Model)
**Unlocks:** All complex UI composition, responsive applications on tiling WMs

---

## 1. Problem Statement

Thuja's current layout system (`columns`, `rows`, `grid`) uses fixed proportions (`Fraction`) or fixed sizes (`Absolute`). There is no:

- **Content-aware sizing:** Columns can't auto-size to their content
- **Minimum size guarantees:** Elements can shrink to zero in narrow terminals
- **Collapse behavior:** No way to hide or reorganize elements below a width threshold
- **Overflow strategy:** When content doesn't fit, there's no systematic response

On tiling window managers like Hyprland, terminal windows are constantly resized — split, stacked, moved between monitors of different DPI. A TUI framework must handle this gracefully, similar to responsive web design but adapted to the character grid model of terminals.

## 2. Reference Analysis

### Rich (Python) — Layout Class

Rich's Layout divides terminal space into a tree of named regions:
- `split_column()` / `split_row()` for vertical/horizontal splits
- Sizing: `size` (fixed), `ratio` (proportional, default 1), `minimum_size`
- Allocation: fixed sizes first, remaining distributed by ratio with minimum floors
- `visible` property to show/hide sections dynamically

### Textual (Python) — CSS Layout

Textual implements CSS-like layout for terminals:
- Layout modes: `vertical` (flexbox column), `horizontal` (flexbox row), `grid`
- Sizing: `fr` (fractional), `%`, fixed cells, `auto` (content-based)
- Box model: content → padding → border → margin
- Overflow: `hidden`, `scroll`, `auto`
- Min/max width and height constraints

### Thuja (Current)

```fsharp
type LayoutProps = Absolute of size: int | Fraction of value: int

columns : LayoutProps list -> (Region -> ViewTree) list -> Region -> ViewTree
rows : LayoutProps list -> (Region -> ViewTree) list -> Region -> ViewTree
grid : LayoutProps list -> LayoutProps list -> ... -> Region -> ViewTree
```

Algorithm: Sum fractions, subtract absolute sizes, distribute remainder proportionally. No measurement, no minimums, no overflow handling.

## 3. F# Native Design

### Enhanced Layout Props

```fsharp
/// Sizing specification for a layout slot.
type Size =
    | Fixed of cells: int               // Exact size in cells/lines
    | Fr of weight: int                 // Fractional share of remaining space
    | Auto                              // Size to content (via Measure protocol)
    | MinMax of min: int * max: int * inner: Size  // Constrained sizing
    | Percent of pct: int               // Percentage of available space

/// Overflow behavior when content exceeds allocated space.
type Overflow =
    | Clip          // Hard truncation at boundary
    | Wrap          // Reflow content (text wrapping)
    | Ellipsis      // Truncate with "..." indicator
    | Hidden        // Don't render overflow (reserve space)

/// Visibility control for responsive breakpoints.
type Visibility =
    | Visible                          // Always shown
    | HiddenBelow of minWidth: int     // Hidden when container width < threshold
    | CollapseTo of Size               // Switches to alternate size below threshold

/// A layout slot specification combining sizing, overflow, and visibility.
type LayoutSlot = {
    Size: Size
    Overflow: Overflow
    Visibility: Visibility
}

module LayoutSlot =
    /// Default slot: fractional with clip overflow, always visible.
    let fr n = { Size = Fr n; Overflow = Clip; Visibility = Visible }
    let fixed n = { Size = Fixed n; Overflow = Clip; Visibility = Visible }
    let auto = { Size = Auto; Overflow = Wrap; Visibility = Visible }
```

### Space Allocation Algorithm

The layout allocation algorithm runs in phases:

```fsharp
module LayoutAllocator =
    /// Allocate space to layout slots given available width and children.
    let allocate
        (available: int)
        (slots: LayoutSlot list)
        (children: IMeasurable option list)  // None if child doesn't implement IMeasurable
        : int list =                          // allocated width per slot

        // Phase 0: Visibility filtering
        // Remove slots where Visibility = HiddenBelow and available < threshold
        // Collapse slots where CollapseTo applies

        // Phase 1: Fixed allocation
        // Fixed slots get their exact size (clamped to available)
        // Percent slots get (available * pct / 100)

        // Phase 2: Auto measurement
        // Auto slots: call child.Measure(available) to get (min, max)
        // Start with maximum, record minimum as floor

        // Phase 3: Fraction distribution
        // Remaining = available - fixed - auto_allocated
        // Distribute remaining to Fr slots proportionally by weight

        // Phase 4: Overflow resolution
        // If total > available:
        //   a. Shrink Auto slots toward their minimums (proportionally)
        //   b. If still over, shrink Fr slots proportionally
        //   c. If still over, all flexible slots at minimum — content clips
        //
        // If total < available:
        //   a. Expand Auto slots toward their maximums
        //   b. Expand Fr slots proportionally
        //   c. If expand not desired, leave gap (right-aligned, centered, etc.)

        ...
```

### Responsive Breakpoints

```fsharp
/// Define responsive behavior for a layout.
let responsive (breakpoints: (int * LayoutSlot list) list) (children: ...) =
    fun (region: Region) ->
        // Find the largest breakpoint that fits
        let slots =
            breakpoints
            |> List.filter (fun (threshold, _) -> region.Width >= threshold)
            |> List.maxBy fst
            |> snd
        // Allocate using matched breakpoint's slots
        ...
```

Example usage:
```fsharp
// Wide: 3 columns. Medium: 2 columns. Narrow: stacked
responsive [
    (100, [ fr 1; fr 1; fr 1 ])        // >= 100 cols: 3 columns
    (60,  [ fr 1; fr 1 ])               // >= 60 cols: 2 columns
    (0,   [ fr 1 ])                     // < 60 cols: single column (stacked)
] [ sidebar; content; details ]
```

### Updated Layout Functions

```fsharp
/// Horizontal layout with enhanced sizing.
let columns (slots: LayoutSlot list) (children: (Region -> ViewTree) list) : Region -> ViewTree =
    fun region ->
        let measurements = measureChildren slots children region.Width
        let widths = LayoutAllocator.allocate region.Width slots measurements
        let regions = computeRegions region widths Horizontal
        buildViewTree region regions children

/// Vertical layout with enhanced sizing.
let rows (slots: LayoutSlot list) (children: (Region -> ViewTree) list) : Region -> ViewTree =
    fun region ->
        let measurements = measureChildren slots children region.Height
        let heights = LayoutAllocator.allocate region.Height slots measurements
        let regions = computeRegions region heights Vertical
        buildViewTree region regions children

/// Grid layout with column and row sizing.
let grid
    (colSlots: LayoutSlot list)
    (rowSlots: LayoutSlot list)
    (cells: (Region -> ViewTree) list list)
    : Region -> ViewTree = ...
```

### Backward Compatibility

The old API is preserved as sugar:

```fsharp
// These still work exactly as before
type LayoutProps = Absolute of int | Fraction of int

let columns (props: LayoutProps list) =
    let slots = props |> List.map (function
        | Absolute n -> LayoutSlot.fixed n
        | Fraction n -> LayoutSlot.fr n)
    Columns.create slots
```

## 4. Integration with Thuja Core

### View Function

The view function receives a Region that reflects current terminal size. When the terminal resizes, the Elm loop calls view with the new Region. Layout elements use the Region's dimensions to drive allocation, measurement, and breakpoint evaluation.

No changes to the Elm loop itself — responsiveness is entirely within the view function.

### ViewTree Diffing

Layout changes due to resize produce different ViewTrees. The structural diff engine detects all changes and re-renders affected elements. This is more efficient than Rich's full-redraw approach — only the elements whose allocated space actually changed are re-rendered.

### Region Subdivision

The `computeRegions` function divides a parent Region into child Regions based on allocated sizes:

```fsharp
let computeRegions (parent: Region) (sizes: int list) (direction: Direction) : Region list =
    let mutable offset = match direction with Horizontal -> parent.X1 | Vertical -> parent.Y1
    sizes |> List.map (fun size ->
        let region =
            match direction with
            | Horizontal -> Region.create offset parent.Y1 (offset + size - 1) parent.Y2
            | Vertical -> Region.create parent.X1 offset parent.X2 (offset + size - 1)
        offset <- offset + size
        region)
```

## 5. Responsive Behavior

### Terminal Resize Flow

```
1. Hyprland resizes terminal window
2. Backend detects new TerminalSize
3. Program.run creates new root Region
4. view model newRegion called
5. Layout elements:
   a. Evaluate visibility breakpoints → filter visible children
   b. Call Measure on Auto children with new available width
   c. Run allocation algorithm with new constraints
   d. Subdivide Region into child Regions
6. Children render into their new Regions
7. ViewTree diff detects changes
8. Only changed elements write to terminal
```

### Progressive Enhancement

```fsharp
// A sidebar that collapses gracefully
let sidebar =
    { Size = Auto
      Overflow = Clip
      Visibility = CollapseTo (Fixed 1) }  // collapse to 1-char icon strip

// A details panel that disappears on narrow terminals
let details =
    { Size = Fr 1
      Overflow = Wrap
      Visibility = HiddenBelow 80 }  // hide below 80 columns

columns [ sidebar; LayoutSlot.fr 2; details ] [ ... ]
```

### The Character Grid Reality

Unlike CSS where sub-pixel rendering smooths proportional layout, terminal layout is quantized to integer character cells. The allocator must handle:

- **Rounding:** Fractional cell counts rounded to integers, with remainder distributed to the last slot (or largest slot) to prevent gaps
- **Minimum 1:** Visible slots get at minimum 1 cell (unless hidden)
- **Border overhead:** Panels consume 2 cells (left + right border) before content
- **Gap handling:** Optional separator columns between slots (configurable width)

## 6. API Surface

### Simple Cases (Backward Compatible)

```fsharp
// Existing API works unchanged
columns [ Fraction 1; Fraction 2; Absolute 20 ] [ child1; child2; child3 ]
rows [ Fraction 1; Absolute 3 ] [ content; statusBar ]
```

### Enhanced Sizing

```fsharp
// Auto-size first column to content, rest proportional
columns [ LayoutSlot.auto; LayoutSlot.fr 1; LayoutSlot.fr 1 ] [ label; input; help ]

// Constrained sizing
columns [
    { Size = MinMax (20, 40, Auto); Overflow = Clip; Visibility = Visible }
    LayoutSlot.fr 1
] [ sidebar; main ]

// Responsive breakpoints
responsive [
    (120, [ LayoutSlot.fixed 30; LayoutSlot.fr 1; LayoutSlot.fixed 30 ])
    (80,  [ LayoutSlot.fixed 20; LayoutSlot.fr 1 ])
    (40,  [ LayoutSlot.fr 1 ])
] [ nav; content; aside ]
```

## 7. .NET-Free Design Notes

- `Size`, `Overflow`, `Visibility`, `LayoutSlot` are plain F# DUs and records
- `LayoutAllocator.allocate` is a pure function: `int * LayoutSlot list * Measurement option list -> int list`
- No threading: layout is synchronous, deterministic, runs during view construction
- No mutable state beyond local variables in the allocator (can be made fully functional)
- Region subdivision is pure arithmetic
- Responsive breakpoints are pure pattern matching on dimensions
- No `System.Math` dependency needed (just `min`, `max`, integer arithmetic)

## 8. Acceptance Criteria

- [ ] `Size` type supports: `Fixed`, `Fr`, `Auto`, `MinMax`, `Percent`
- [ ] `LayoutSlot` combines sizing, overflow, and visibility
- [ ] `LayoutAllocator.allocate` handles all sizing combinations correctly
- [ ] Auto sizing uses IMeasurable when available, fills space otherwise
- [ ] Fr distribution handles rounding without gaps
- [ ] MinMax constraints clamped correctly
- [ ] Overflow phase: over-budget shrinks flexible slots proportionally
- [ ] Under-budget: flexible slots expand proportionally
- [ ] `Visibility.HiddenBelow` removes slots below threshold
- [ ] `Visibility.CollapseTo` substitutes alternate sizing below threshold
- [ ] `responsive` function selects breakpoint by available width
- [ ] Backward compatibility: existing `Fraction`/`Absolute` API unchanged
- [ ] ViewTree diffing correctly handles layout changes on resize
- [ ] Integer rounding tested for edge cases (very narrow, single-cell, zero)
- [ ] No `System.*` imports in layout code
