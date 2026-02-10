# PRD-001: Segment Model

**Status:** Draft
**Tier:** 0, Foundation
**Dependencies:** None (parallel with PRD-000)
**Unlocks:** PRD-010, PRD-012, PRD-013, PRD-020, PRD-024, and all complex widget rendering

---

## 1. Problem Statement

Thuja's current rendering pipeline produces `Command list` values, which are low-level positioned instructions (`MoveTo`, `Print`, `PrintWith`). Each element independently computes absolute cursor positions and styled strings. This works for simple elements but creates problems:

- **No composition:** A Table cannot render a cell containing a Panel containing styled Text without each layer knowing about absolute coordinates
- **No splitting:** There's no way to take a stream of rendered content and divide it at column boundaries (needed for tables, multi-column layouts)
- **No post-processing:** You can't apply a style overlay, crop, pad, or align rendered content after the fact
- **String manipulation is fragile:** Elements do raw string truncation/padding, which breaks with wide characters (CJK, emoji) that occupy 2 terminal cells

Rich and Spectre.Console solve this with a `Segment` abstraction: an intermediate representation between elements and terminal commands that supports composition operations.

## 2. Reference Analysis

### Rich (Python)

```python
class Segment(NamedTuple):
    text: str
    style: Optional[Style] = None
    control: Optional[Sequence[ControlCode]] = None
```

Key composition operations (all operate on segment iterables):
- `split_lines()`: flat stream to `List[List[Segment]]` by newlines
- `split_and_crop_lines(length)`: split + pad/crop each line to exact cell width
- `adjust_line_length(line, length)`: pad or crop a single line
- `divide(cuts)`: split stream at column positions (Table column layout)
- `apply_style(segments, style)`: layer additional styling onto segments
- `simplify(segments)`: merge adjacent same-style segments
- `set_shape(lines, width, height)`: create fixed rectangular grid
- `align_top/middle/bottom(lines, width, height)`: vertical alignment

The `divide()` operation is particularly important: it takes a line of segments and a list of column positions, and splits the segments at those positions, correctly handling multi-cell characters.

### Spectre.Console (C#)

Nearly identical model. The `Segment` class carries text, style, and a control flag. Same composition pattern. Adds `CellCount()` for wide character awareness.

### Thuja (Current)

No segment model. Elements produce `Command list` directly:
```fsharp
type Command =
    | MoveTo of x: int * y: int
    | Print of content: string
    | PrintWith of style: Style * content: string
```

This is the *output* of what segments would produce: positioned, styled writes. The problem is there is no intermediate layer where composition can happen.

## 3. F# Native Design

### Core Type

```fsharp
/// A Segment is the atomic unit of rendered content.
/// Segments are unstyled or styled text fragments that compose into lines.
[<Struct>]
type Segment =
    | Text of text: string * style: Style voption
    | LineBreak
    | Control of code: ControlCode

/// Terminal control codes (cursor movement, screen operations).
type ControlCode =
    | CursorUp of int
    | CursorDown of int
    | CursorForward of int
    | CursorBackward of int
    | CursorMoveTo of x: int * y: int
    | CursorHome
    | EraseInLine
    | Clear
    | HideCursor
    | ShowCursor

/// A Line is a list of segments that occupy a single terminal row.
type Line = Segment list
```

### Cell Width Awareness

Wide characters (CJK, some emoji) occupy 2 terminal cells. The segment model must account for this:

```fsharp
module CellWidth =
    /// Calculate the display cell width of a string.
    /// ASCII = 1 cell, CJK/wide = 2 cells, zero-width combiners = 0 cells.
    let measure (s: string) : int = ...

    /// Truncate a string to fit within a cell width, respecting character boundaries.
    let truncate (maxCells: int) (s: string) : string * int = ...
```

This replaces `String.Length` throughout the codebase. Every place that currently uses character count for layout must use cell width instead.

### Composition Module

```fsharp
module Segment =
    /// Calculate the cell width of a segment.
    let cellWidth = function
        | Text (t, _) -> CellWidth.measure t
        | LineBreak -> 0
        | Control _ -> 0

    /// Calculate total cell width of a segment list.
    let totalWidth (segments: Segment list) : int =
        segments |> List.sumBy cellWidth

module Line =
    /// Split a flat segment stream into lines at LineBreak segments.
    let splitLines (segments: Segment list) : Line list = ...

    /// Pad or crop a line to an exact cell width.
    /// Cropping respects character boundaries (won't split a wide char).
    /// Padding uses unstyled spaces.
    let adjustWidth (width: int) (line: Line) : Line = ...

    /// Split a line at specified column positions.
    /// Returns a list of sub-lines, one per column.
    /// Handles wide characters at split points correctly.
    let divide (cuts: int list) (line: Line) : Line list = ...

    /// Apply an additional style to all segments in a line.
    /// New style is merged with existing segment styles.
    let applyStyle (style: Style) (line: Line) : Line = ...

    /// Merge adjacent segments with identical styles.
    let simplify (line: Line) : Line = ...

module Lines =
    /// Split flat segments into lines and crop/pad each to exact width.
    let shape (width: int) (segments: Segment list) : Line list =
        segments |> Line.splitLines |> List.map (Line.adjustWidth width)

    /// Create a rectangular grid of lines with exact dimensions.
    /// Pads with empty lines if too few, crops if too many.
    let setShape (width: int) (height: int) (segments: Segment list) : Line list =
        let lines = shape width segments
        let padded = lines @ List.replicate (max 0 (height - List.length lines)) []
        padded |> List.truncate height

    /// Vertical alignment within a fixed height.
    let alignVertical (align: Align) (width: int) (height: int) (lines: Line list) : Line list =
        let emptyLine = [ Text (String.replicate width " ", ValueNone) ]
        let content = lines |> List.map (Line.adjustWidth width)
        let gap = max 0 (height - List.length content)
        match align with
        | Top | TopLeft | TopRight ->
            content @ List.replicate gap emptyLine
        | Bottom | BottomLeft | BottomRight ->
            List.replicate gap emptyLine @ content
        | Center | _ ->
            let top = gap / 2
            let bot = gap - top
            List.replicate top emptyLine @ content @ List.replicate bot emptyLine
        |> List.truncate height
```

## 4. Integration with Thuja Core

### Rendering Pipeline Change

The rendering pipeline gains an intermediate stage:

**Current:**
```
Element.Render(region) -> Command list -> Backend.Execute
```

**With Segments:**
```
Element.RenderSegments(width, height) -> Segment list
    -> Lines.shape / Lines.setShape  (composition layer)
    -> Lines.toCommands(region)      (position segments into Commands)
    -> Backend.Execute
```

### IElement Evolution

Elements can optionally produce segments instead of commands:

```fsharp
/// Elements that render via the segment model.
type ISegmentRenderable =
    /// Render the element as a stream of segments within the given dimensions.
    abstract RenderSegments: width: int * height: int -> Segment list
```

Container elements use this interface to render children, compose the results (split, crop, align), then either:
- Pass composed segments up (if they're also ISegmentRenderable)
- Convert to positioned Commands via `Lines.toCommands` for the final IElement.Render

### Bridge: Segments to Commands

```fsharp
module Lines =
    /// Convert shaped lines to positioned Commands within a Region.
    let toCommands (region: Region) (lines: Line list) : Command list =
        lines
        |> List.mapi (fun row line ->
            let y = region.Y1 + row
            if y > region.Y2 then []
            else
                [ yield MoveTo (region.X1, y)
                  for seg in line do
                    match seg with
                    | Text (t, ValueSome style) -> yield PrintWith (style, t)
                    | Text (t, ValueNone) -> yield Print t
                    | _ -> () ])
        |> List.concat
```

### Migration Path

Existing elements continue to work via `IElement.Render`. New and updated elements implement `ISegmentRenderable` for composability. Container elements (Table, Panel, Layout) are the primary consumers: they render children as segments, compose, then convert to commands.

## 5. Responsive Behavior

Segments decouple content generation from spatial positioning. When a terminal resizes:

1. New Region dimensions flow into element rendering
2. Elements produce segments for new width/height
3. `Lines.shape` crops/pads to exact dimensions
4. `Line.divide` re-splits table columns for new widths
5. Content reflows naturally because segments are width-independent until shaped

The segment model makes responsive behavior automatic for any element that produces segments. The shaping operations handle the adaptation.

## 6. API Surface

### For Element Authors

```fsharp
// A panel that renders its child as segments, adds borders, outputs commands
type Panel = ...
    interface IElement with
        member this.Render(region) =
            let innerWidth = region.Width - 2  // borders
            let innerHeight = region.Height - 2
            // Render child as segments
            let childSegments = renderChild innerWidth innerHeight
            // Shape to inner dimensions
            let lines = Lines.setShape innerWidth innerHeight childSegments
            // Convert to commands at inner region position
            let innerRegion = region.Inner
            let contentCmds = Lines.toCommands innerRegion lines
            // Add border commands
            borderCmds @ contentCmds
```

### For Composition

```fsharp
// Table rendering: divide a row of segments into column cells
let renderRow (columnWidths: int list) (cells: Segment list list) =
    let mergedLine = cells |> List.concat
    let cuts = columnWidths |> List.scan (+) 0 |> List.tail
    Line.divide cuts mergedLine
```

## 7. .NET-Free Design Notes

- `Segment` is a struct DU, pure F# with no heap allocation for simple cases
- `ControlCode` is a plain DU with no System.Console dependency
- `CellWidth` module is pure computation over characters. The Unicode width table is data, not a BCL call (replaces any dependency on `System.Globalization`)
- All composition functions are pure `list -> list` transforms
- No IO, no threading, no mutable state
- The `Lines.toCommands` bridge produces Thuja's existing `Command` type, which the backend translates to actual terminal writes. The segment layer never touches IO

### CellWidth Implementation Note

The `CellWidth.measure` function needs Unicode East Asian Width data. This should be:
- A compile-time lookup table generated from Unicode EAW data
- Pure function: `char -> int` (or `Rune -> int` for full Unicode)
- No dependency on `System.Globalization.StringInfo` or similar BCL types
- The table is ~200 range entries, trivial to embed as F# match expressions

## 8. Acceptance Criteria

- [ ] `Segment` type defined (Text with optional Style, LineBreak, Control)
- [ ] `ControlCode` DU covering cursor movement and screen operations
- [ ] `CellWidth` module with `measure` and `truncate` (Unicode-aware)
- [ ] `Line.splitLines` correctly handles segments containing newlines
- [ ] `Line.adjustWidth` pads and crops respecting cell widths
- [ ] `Line.divide` splits at column positions handling wide characters
- [ ] `Line.applyStyle` merges styles onto existing segments
- [ ] `Line.simplify` merges adjacent same-style segments
- [ ] `Lines.shape` and `Lines.setShape` produce exact rectangular output
- [ ] `Lines.alignVertical` handles Top/Center/Bottom alignment
- [ ] `Lines.toCommands` correctly bridges to positioned Command output
- [ ] At least one element (Text) migrated to produce segments
- [ ] Panel or Table uses segment composition for child rendering
- [ ] No `System.*` imports in segment-related code
- [ ] Wide character handling verified with CJK and emoji test cases
