# PRD-014: Border Style Expansion

**Status:** Draft
**Tier:** 1 — Core Capability
**Dependencies:** None (can be implemented independently)
**Unlocks:** Visual richness for panels, tables, rules across the entire widget catalog

---

## 1. Problem Statement

Thuja currently defines 4 border styles: `Normal`, `Rounded`, `Thick`, `Double`. Each maps to a fixed set of Unicode box-drawing characters. Spectre.Console offers 18 table border styles and 6 box border styles. Rich has similar variety.

For a "Rich-level" TUI experience, visual variety in borders is low-effort, high-impact. Borders appear on panels, tables, rules, tree guides, and layout dividers. Expanding the catalog creates immediate visual polish.

## 2. Reference Analysis

### Spectre.Console Border Styles

**Box borders (panels):** ASCII, Square, Rounded, Heavy, Double, None

**Table borders:** ASCII, Ascii2, AsciiDoubleHead, Square, SquareDoubleHead, Rounded, Minimal, MinimalHeavyHead, MinimalDoubleHead, Heavy, HeavyHead, HeavyEdge, Double, DoubleEdge, Simple, SimpleHeavy, Horizontal, Markdown, None

### Rich (Python)

Box styles in `box.py`: ASCII, ASCII2, ASCII_DOUBLE_HEAD, SQUARE, SQUARE_DOUBLE_HEAD, MINIMAL, MINIMAL_HEAVY_HEAD, MINIMAL_DOUBLE_HEAD, SIMPLE, SIMPLE_HEAD, SIMPLE_HEAVY, HORIZONTALS, ROUNDED, HEAVY, HEAVY_EDGE, HEAVY_HEAD, DOUBLE, DOUBLE_EDGE, MARKDOWN

Each style defines 8 characters: top-left, top, top-right, left, right, bottom-left, bottom, bottom-right. Table styles additionally define: horizontal divider, cross junction, left-T, right-T, top-T, bottom-T.

### Thuja (Current)

```fsharp
type BorderStyle = Normal | Rounded | Thick | Double
```

4 styles with characters defined in `Border.Styles` map. Each style maps to: Horizontal, Vertical, TopLeft, TopRight, BottomLeft, BottomRight.

## 3. F# Native Design

### Border Character Set

```fsharp
/// Complete set of characters for rendering a box border.
type BoxBorder = {
    TopLeft: string
    Top: string
    TopRight: string
    Left: string
    Right: string
    BottomLeft: string
    Bottom: string
    BottomRight: string
}

/// Extended character set for table borders (adds internal dividers).
type TableBorder = {
    Box: BoxBorder                     // Outer border
    HeaderDivider: string option       // Horizontal line below header
    RowDivider: string option          // Horizontal line between rows
    ColumnDivider: string option       // Vertical line between columns
    CrossJunction: string option       // Where row and column dividers cross
    TopTee: string option              // Top edge column divider
    BottomTee: string option           // Bottom edge column divider
    LeftTee: string option             // Left edge row divider
    RightTee: string option            // Right edge row divider
    HeaderCrossJunction: string option // Cross at header divider (may differ)
    HeaderLeftTee: string option
    HeaderRightTee: string option
}
```

### Built-in Styles

```fsharp
module BoxBorders =
    let none = { TopLeft = " "; Top = " "; TopRight = " "
                 Left = " "; Right = " "
                 BottomLeft = " "; Bottom = " "; BottomRight = " " }

    let ascii = { TopLeft = "+"; Top = "-"; TopRight = "+"
                  Left = "|"; Right = "|"
                  BottomLeft = "+"; Bottom = "-"; BottomRight = "+" }

    let square = { TopLeft = "┌"; Top = "─"; TopRight = "┐"
                   Left = "│"; Right = "│"
                   BottomLeft = "└"; Bottom = "─"; BottomRight = "┘" }

    let rounded = { TopLeft = "╭"; Top = "─"; TopRight = "╮"
                    Left = "│"; Right = "│"
                    BottomLeft = "╰"; Bottom = "─"; BottomRight = "╯" }

    let heavy = { TopLeft = "┏"; Top = "━"; TopRight = "┓"
                  Left = "┃"; Right = "┃"
                  BottomLeft = "┗"; Bottom = "━"; BottomRight = "┛" }

    let double = { TopLeft = "╔"; Top = "═"; TopRight = "╗"
                   Left = "║"; Right = "║"
                   BottomLeft = "╚"; Bottom = "═"; BottomRight = "╝" }

    let minimal = { TopLeft = " "; Top = " "; TopRight = " "
                    Left = " "; Right = " "
                    BottomLeft = " "; Bottom = " "; BottomRight = " " }
    // minimal shows only internal dividers (header line, column separators)

    let simple = { TopLeft = " "; Top = " "; TopRight = " "
                   Left = " "; Right = " "
                   BottomLeft = " "; Bottom = " "; BottomRight = " " }

module TableBorders =
    let none = { Box = BoxBorders.none; HeaderDivider = None; ... }

    let ascii = { Box = BoxBorders.ascii
                  HeaderDivider = Some "-"; RowDivider = Some "-"
                  ColumnDivider = Some "|"; CrossJunction = Some "+"
                  TopTee = Some "+"; BottomTee = Some "+"
                  LeftTee = Some "+"; RightTee = Some "+"
                  HeaderCrossJunction = Some "+"; HeaderLeftTee = Some "+"
                  HeaderRightTee = Some "+" }

    let square = { Box = BoxBorders.square
                   HeaderDivider = Some "─"; RowDivider = Some "─"
                   ColumnDivider = Some "│"; CrossJunction = Some "┼"
                   TopTee = Some "┬"; BottomTee = Some "┴"
                   LeftTee = Some "├"; RightTee = Some "┤"
                   HeaderCrossJunction = Some "┼"; HeaderLeftTee = Some "├"
                   HeaderRightTee = Some "┤" }

    let rounded = { Box = BoxBorders.rounded
                    HeaderDivider = Some "─"; RowDivider = None
                    ColumnDivider = Some "│"; CrossJunction = Some "┼"
                    TopTee = Some "┬"; BottomTee = Some "┴"
                    LeftTee = Some "├"; RightTee = Some "┤"
                    HeaderCrossJunction = Some "┼"; HeaderLeftTee = Some "├"
                    HeaderRightTee = Some "┤" }

    let heavy = { Box = BoxBorders.heavy
                  HeaderDivider = Some "━"; RowDivider = Some "━"
                  ColumnDivider = Some "┃"; CrossJunction = Some "╋"
                  TopTee = Some "┳"; BottomTee = Some "┻"
                  LeftTee = Some "┣"; RightTee = Some "┫"
                  HeaderCrossJunction = Some "╋"; HeaderLeftTee = Some "┣"
                  HeaderRightTee = Some "┫" }

    let heavyHead = { Box = BoxBorders.square
                      HeaderDivider = Some "━"
                      HeaderCrossJunction = Some "╈"
                      HeaderLeftTee = Some "┠"; HeaderRightTee = Some "┨"
                      RowDivider = Some "─"; ColumnDivider = Some "│"
                      CrossJunction = Some "┼"
                      TopTee = Some "┬"; BottomTee = Some "┴"
                      LeftTee = Some "├"; RightTee = Some "┤" }

    let double = { Box = BoxBorders.double
                   HeaderDivider = Some "═"; RowDivider = Some "═"
                   ColumnDivider = Some "║"; CrossJunction = Some "╬"
                   TopTee = Some "╦"; BottomTee = Some "╩"
                   LeftTee = Some "╠"; RightTee = Some "╣"
                   HeaderCrossJunction = Some "╬"; HeaderLeftTee = Some "╠"
                   HeaderRightTee = Some "╣" }

    let minimal = { Box = BoxBorders.none
                    HeaderDivider = Some "─"; RowDivider = None
                    ColumnDivider = Some "│"; CrossJunction = Some "┼"
                    TopTee = None; BottomTee = None
                    LeftTee = None; RightTee = None
                    HeaderCrossJunction = Some "┼"; HeaderLeftTee = None
                    HeaderRightTee = None }

    let markdown = { Box = BoxBorders.none
                     HeaderDivider = Some "-"; RowDivider = None
                     ColumnDivider = Some "|"; CrossJunction = Some "|"
                     TopTee = None; BottomTee = None
                     LeftTee = Some "|"; RightTee = Some "|"
                     HeaderCrossJunction = Some "|"; HeaderLeftTee = Some "|"
                     HeaderRightTee = Some "|" }

    // ... additional styles: horizontal, simpleHeavy, doubleEdge, etc.
```

## 4. Integration with Thuja Core

### Panel.fs

Replace `BorderStyle` DU with `BoxBorder` record in panel props:

```fsharp
type PanelProps =
    | Border of BoxBorder       // was: BorderStyle of BorderStyle
    | BorderColor of Color
    | Title of string           // future: supports markup (PRD-010)
```

### Table.fs

Tables use `TableBorder` for full internal divider support.

### Rule.fs

Rules use the `Top` character from a `BoxBorder` for horizontal rules, `Left` for vertical rules.

### Migration

The old `BorderStyle` DU maps to new types:

```fsharp
let fromLegacy = function
    | Normal -> BoxBorders.square
    | Rounded -> BoxBorders.rounded
    | Thick -> BoxBorders.heavy
    | Double -> BoxBorders.double
```

## 5. Responsive Behavior

Borders are constant-width (1 cell per edge). They don't need responsive behavior themselves, but they consume space that the layout system must account for when allocating child regions (2 cells horizontal, 2 cells vertical for a bordered panel).

When a panel is too narrow for its border + minimum content, the border should degrade gracefully: switch to `None` border (borderless) rather than producing garbled output.

## 6. API Surface

```fsharp
// Panels with different borders
panel [ Border BoxBorders.rounded ] [ text [] "Rounded" ]
panel [ Border BoxBorders.heavy; BorderColor (Named Red) ] [ text [] "Heavy red" ]
panel [ Border BoxBorders.double ] [ text [] "Double" ]

// Tables with border styles
table [ TableBorderStyle TableBorders.markdown ] headers rows
table [ TableBorderStyle TableBorders.minimal ] headers rows
table [ TableBorderStyle TableBorders.heavyHead ] headers rows

// Custom borders
let custom = { TopLeft = "╒"; Top = "═"; TopRight = "╕"
               Left = "│"; Right = "│"
               BottomLeft = "╘"; Bottom = "═"; BottomRight = "╛" }
panel [ Border custom ] [ text [] "Mixed" ]
```

## 7. .NET-Free Design Notes

- `BoxBorder` and `TableBorder` are plain F# records of strings
- Built-in styles are compile-time constants — no file loading, no resources
- Border rendering is pure string concatenation into Commands
- No Unicode library dependency — characters are string literals
- No `System.Char`, `System.Text` or other BCL types

## 8. Acceptance Criteria

- [ ] `BoxBorder` record type with 8 character fields
- [ ] `TableBorder` record type with full junction characters
- [ ] At least 8 box border styles (none, ascii, square, rounded, heavy, double, minimal, simple)
- [ ] At least 12 table border styles (matching Spectre.Console's most useful)
- [ ] `markdown` table border style for Markdown-compatible output
- [ ] Panel.fs updated to use `BoxBorder` record
- [ ] Table.fs updated to use `TableBorder` record with proper junctions
- [ ] Rule.fs updated to use border characters from `BoxBorder`
- [ ] Backward compatibility: existing `Normal`/`Rounded`/`Thick`/`Double` map to new types
- [ ] Custom borders: users can define their own character sets
- [ ] No BCL dependencies in border definitions
