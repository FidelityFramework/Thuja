# PRD-023: Canvas (Pixel Drawing)

**Status:** Draft
**Tier:** 2, Widget Catalog
**Dependencies:** PRD-001 (Segment), PRD-002 (Color)
**Unlocks:** Graphs, sparklines, custom visualizations, pixel art, heatmaps

---

## 1. Problem Statement

Some visualizations require pixel-level control: plotting data points, rendering sparklines, drawing custom graphics, and displaying heatmaps. Terminal "pixels" use half-block characters (`▀▄█`) to achieve 2x vertical resolution within a single character cell. Both Rich and Spectre.Console provide canvas widgets for this.

## 2. Reference Analysis

### Rich / Spectre.Console

**Canvas:**
- `Canvas(width, height)` creates a pixel grid
- `SetPixel(x, y, color)` sets individual pixels
- Rendering uses half-block characters: each character cell = 2 vertical pixels
- Top half = foreground color, bottom half = background color of `▄`
- `MaxWidth`, `PixelWidth` (console chars per pixel)

**Spectre.Console additionally:**
- `CanvasImage` renders actual image files via ImageSharp library

## 3. F# Native Design

### Canvas Model

```fsharp
/// A pixel grid with color values.
type Canvas = {
    Width: int                              // Pixel width
    Height: int                             // Pixel height
    Pixels: Color option array              // Row-major pixel data (None = transparent)
}

module Canvas =
    /// Create an empty canvas.
    let create (width: int) (height: int) : Canvas =
        { Width = width; Height = height
          Pixels = Array.create (width * height) None }

    /// Set a pixel color. Returns new canvas (functional update).
    let setPixel (x: int) (y: int) (color: Color) (canvas: Canvas) : Canvas =
        if x >= 0 && x < canvas.Width && y >= 0 && y < canvas.Height then
            let pixels = Array.copy canvas.Pixels
            pixels.[y * canvas.Width + x] <- Some color
            { canvas with Pixels = pixels }
        else canvas

    /// Get a pixel color.
    let getPixel (x: int) (y: int) (canvas: Canvas) : Color option =
        if x >= 0 && x < canvas.Width && y >= 0 && y < canvas.Height then
            canvas.Pixels.[y * canvas.Width + x]
        else None

    /// Draw a horizontal line.
    let hline (x1: int) (x2: int) (y: int) (color: Color) (canvas: Canvas) : Canvas = ...

    /// Draw a vertical line.
    let vline (x: int) (y1: int) (y2: int) (color: Color) (canvas: Canvas) : Canvas = ...

    /// Draw a rectangle outline.
    let rect (x: int) (y: int) (w: int) (h: int) (color: Color) (canvas: Canvas) : Canvas = ...

    /// Fill a rectangle.
    let fillRect (x: int) (y: int) (w: int) (h: int) (color: Color) (canvas: Canvas) : Canvas = ...
```

### Rendering

```fsharp
/// Render canvas to segments using half-block characters.
/// Each character cell represents 2 vertical pixels:
///   Top pixel = foreground color of "▀"
///   Bottom pixel = background color of "▀"
///   Or equivalently: foreground of "▄" is bottom, background is top.
let renderCanvas (canvas: Canvas) : Segment list =
    let charHeight = (canvas.Height + 1) / 2  // 2 pixels per char row
    [ for row in 0 .. charHeight - 1 do
        for col in 0 .. canvas.Width - 1 do
            let topPixel = getPixel col (row * 2) canvas
            let botPixel = getPixel col (row * 2 + 1) canvas
            match topPixel, botPixel with
            | None, None -> yield Text (" ", ValueNone)
            | Some top, None ->
                yield Text ("▀", ValueSome { Foreground = top; Background = Default; Attributes = [] })
            | None, Some bot ->
                yield Text ("▄", ValueSome { Foreground = bot; Background = Default; Attributes = [] })
            | Some top, Some bot ->
                yield Text ("▄", ValueSome { Foreground = bot; Background = top; Attributes = [] })
        yield LineBreak ]
```

### Canvas Element

```fsharp
type CanvasProps =
    | PixelWidth of int     // Console chars per pixel (default: 2 for square-ish pixels)
    | Scale                 // Auto-scale to fit region

/// Render a canvas element.
let canvas (props: CanvasProps list) (data: Canvas) : Region -> ViewTree = ...
```

### Sparkline (Built on Canvas)

```fsharp
/// Render a sparkline from data points.
let sparkline (color: Color) (values: float list) : Region -> ViewTree =
    fun region ->
        let width = region.Width
        let height = region.Height * 2  // half-block doubles resolution
        let maxVal = values |> List.max
        let points = values |> List.mapi (fun i v ->
            let x = i * width / List.length values
            let y = height - 1 - int (v / maxVal * float (height - 1))
            (x, y))
        let c = Canvas.create width height
        let c = points |> List.fold (fun acc (x, y) -> Canvas.setPixel x y color acc) c
        renderCanvas c |> Lines.toCommands region
```

## 4. Integration with Thuja Core

Canvas renders via the segment model. Each character cell is a styled segment (the half-block character with foreground/background colors representing the two pixels). Segments are shaped to the Region via `Lines.setShape`.

Measurement: minimum = 1 char wide (2 pixels), maximum = `canvas.Width / pixelWidth`.

## 5. Responsive Behavior

- With `Scale` prop: canvas auto-scales to fit region (nearest-neighbor resampling)
- Without `Scale`: clips to region dimensions
- Sparklines naturally adapt, plotting `width` data points across available width
- Half-block rendering gives 2x vertical resolution without extra width

## 6. API Surface

```fsharp
// Direct pixel drawing
let myCanvas =
    Canvas.create 40 20
    |> Canvas.fillRect 5 5 10 10 (Named Red)
    |> Canvas.rect 0 0 40 20 (Named White)
    |> Canvas.hline 0 39 10 (Named Yellow)

canvas [ Scale ] myCanvas

// Sparkline
sparkline (Named Green) [ 1.0; 3.0; 2.0; 5.0; 4.0; 6.0; 3.0; 7.0; 5.0; 8.0 ]

// Heatmap from 2D data
let heatmap data = ... // maps values to color gradient, sets pixels
```

## 7. .NET-Free Design Notes

- `Canvas` is a record with an array. `array` is F# native, not a BCL collection
- All drawing functions are pure: `Canvas -> Canvas` (functional builder pattern)
- Half-block rendering is pure string/style composition
- No `System.Drawing`, no image libraries
- Color values use the Color type (PRD-002), not System.ConsoleColor
- Pixel array is the only mutable structure (array), wrapped in immutable record

### Note on Array vs List

The pixel array uses `array` for O(1) random access (essential for `setPixel`). This is a pragmatic choice. F# arrays compile to any runtime that supports contiguous memory. The array is never exposed; the API is functional (`Canvas -> Canvas`).

## 8. Acceptance Criteria

- [ ] `Canvas` type with pixel grid and O(1) access
- [ ] `setPixel`, `getPixel`, `hline`, `vline`, `rect`, `fillRect` drawing primitives
- [ ] Half-block rendering: 2 vertical pixels per character cell
- [ ] Correct foreground/background color pairing for `▀`/`▄` characters
- [ ] `Scale` prop for auto-fitting to region
- [ ] `sparkline` convenience function for data visualization
- [ ] IMeasurable implementation
- [ ] Sample app: simple drawing or data visualization
- [ ] No `System.Drawing` or image library dependencies
