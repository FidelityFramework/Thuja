# PRD-010: Markup Text System

**Status:** Draft
**Tier:** 1 — Core Capability
**Dependencies:** PRD-001 (Segment Model), PRD-002 (Color System)
**Unlocks:** All widgets that display styled text, PRD-022 (FigletText), PRD-024 (Markdown)

---

## 1. Problem Statement

Thuja's current text styling requires programmatic construction:

```fsharp
text [ Color Red ] "Error"
"Error".With(Color.Red)
```

This works for simple cases but becomes unwieldy for mixed-style text. There's no way to express "Error: file [bold]config.json[/bold] not found" as a single string. Users must break content into multiple elements or use raw ANSI codes.

Rich and Spectre.Console both provide inline markup syntax that lets users embed style directives in text strings. This is fundamental to the "rich terminal" experience — it appears in progress bar labels, panel titles, table cells, tree node labels, and throughout the widget catalog.

## 2. Reference Analysis

### Rich (Python)

Markup syntax: `[style]text[/style]` or `[style]text[/]` (auto-close).

```python
console.print("[bold red]Error:[/] file [underline]config.json[/] not found")
```

Styles combine: `[bold italic blue on white]text[/]`
Background: `[on red]`, `[white on red]`
Decorations: `bold`, `dim`, `italic`, `underline`, `strike`, `blink`, `invert`
Colors: named (`red`), hex (`#ff0000`), rgb (`rgb(255,0,0)`)
Links: `[link=https://...]text[/link]`
Escaping: `\[` for literal bracket, `Markup.escape()` for dynamic content

Rich parses markup into a `Text` object with styled `Span` ranges. The `Text` object then renders to `Segment` objects via `__rich_console__`.

### Spectre.Console (C#)

Nearly identical syntax: `[bold red]text[/]`
Additional: emoji shortcodes `:check_mark:` (separate concern, see PRD later)

### Thuja (Current)

No markup parser. Styled text is `(Style * string)` tuples created via extension methods.

## 3. F# Native Design

### Markup Syntax

```
[style]text[/]         — styled span with auto-close
[style]text[/style]    — styled span with explicit close
[/]                    — close innermost open tag
[[                     — literal [ (escaped)
]]                     — literal ] (escaped)
```

Style syntax within brackets:
```
bold                   — text decoration
italic underline       — multiple decorations (space-separated)
red                    — foreground color (named)
on blue                — background color
bold red on blue       — combined
#FF5733                — hex foreground color
rgb(255,87,51)         — RGB foreground color
on #003366             — hex background color
```

### Parsed Representation

```fsharp
/// A span of styled text — the output of markup parsing.
type StyledSpan = {
    Text: string
    Style: Style
}

/// A sequence of styled spans representing rich text.
type StyledText = StyledSpan list
```

### Parser Design

The markup parser is a pure function: `string -> Result<StyledText, MarkupError>`.

```fsharp
module Markup =
    /// Parse a markup string into styled spans.
    let parse (input: string) : Result<StyledText, MarkupError> = ...

    /// Parse markup, treating errors as plain text (lenient mode).
    let parseOrPlain (input: string) : StyledText = ...

    /// Escape a string for safe embedding in markup.
    let escape (text: string) : string =
        text.Replace("[", "[[").Replace("]", "]]")

    /// Strip all markup tags, returning plain text.
    let stripTags (input: string) : string = ...

    /// Calculate the display length (cell width) of markup text, ignoring tags.
    let displayWidth (input: string) : int = ...

type MarkupError =
    | UnmatchedOpenTag of tag: string * position: int
    | UnmatchedCloseTag of tag: string * position: int
    | InvalidStyle of style: string * position: int
    | UnexpectedEnd
```

### Parser Implementation Sketch

```fsharp
// The parser maintains a style stack — each open tag pushes, close pops.
// Styles compose: [bold][red]text[/][/] → bold+red applied to "text"
//
// Algorithm:
// 1. Scan for [ characters
// 2. If [[, emit literal [
// 3. If [/] or [/name], pop style stack
// 4. If [style], parse style, push onto stack
// 5. Text between tags gets the current composed style (fold of stack)
//
// Style parsing:
// - Split on spaces
// - "on" keyword signals next token is background
// - Known decoration names: bold, dim, italic, underline, strike, blink, invert
// - Remaining tokens are color specs: named, #hex, rgb(r,g,b)
```

### Style Parsing

```fsharp
module StyleParser =
    /// Parse a style string like "bold red on blue" into a Style.
    let parse (spec: string) : Result<Style, string> =
        // Split on whitespace, process tokens left to right
        // "on" flips subsequent color to background
        // Decoration names add to attribute list
        // Color specs set foreground (or background after "on")
        ...
```

### Integration with Segment Model (PRD-001)

Parsed markup converts directly to segments:

```fsharp
module StyledText =
    /// Convert styled spans to a Segment list.
    let toSegments (spans: StyledText) : Segment list =
        spans |> List.map (fun span ->
            Segment.Text (span.Text, ValueSome span.Style))
```

## 4. Integration with Thuja Core

### Text Element Enhancement

The `text` element gains markup support:

```fsharp
// Plain text (existing behavior)
text [] "Hello, world"

// Markup text (new)
markup [] "[bold]Hello[/], [red]world[/]"

// Or as a TextProps option
text [ UseMarkup ] "[bold red]Error:[/] something went wrong"
```

### Panel Titles

```fsharp
panel [ Title "[bold cyan]Server Status[/]" ] [ ... ]
```

### Table Cells

```fsharp
table headers rows  // cells can contain markup strings
```

### Universal Principle

Any place that currently accepts a `string` for display can accept markup. The rendering pipeline parses markup to StyledText, converts to Segments, and composes via the segment model.

## 5. Responsive Behavior

Markup text participates in measurement (PRD-000):

- `displayWidth` calculates cell width ignoring tags
- `Minimum` = longest word (markup-stripped) cell width
- `Maximum` = longest line (markup-stripped) cell width
- Word wrapping respects markup spans — a span can wrap across lines while maintaining its style. The style stack carries across line breaks.

## 6. API Surface

### For Users

```fsharp
// Rich text in views
markup [] "[bold]Name:[/] Claude"
markup [ TextAlign Center ] "[italic dim]Loading...[/]"

// Markup in any string context
panel [ Title "[bold]Dashboard[/]" ] [ ... ]

// Escaping dynamic content
let userName = Markup.escape untrustedInput
markup [] $"[bold]User:[/] {userName}"

// Programmatic construction (existing, still works)
text [ Color (Named Red); Attributes [ Bold ] ] "Error"
```

### For Element Authors

```fsharp
// Parse markup in element rendering
let spans = Markup.parseOrPlain titleString
let segments = StyledText.toSegments spans
// Use segment composition to shape within region
```

## 7. .NET-Free Design Notes

- The parser is pure: `string -> Result<StyledText, MarkupError>`
- No regex dependency (hand-written scanner over character array/indices)
- `StyledSpan` and `StyledText` are plain F# records/lists
- `MarkupError` is a plain DU with position information
- `StyleParser.parse` is pure string processing — no BCL style types
- No `System.Text.RegularExpressions` or similar
- Escape function is pure string replacement

## 8. Acceptance Criteria

- [ ] Markup parser handles: `[style]text[/]`, `[style]text[/style]`, nested tags
- [ ] Style parser handles: decorations, named colors, hex, rgb, `on` backgrounds
- [ ] Combined styles: `[bold italic red on blue]text[/]` produces correct Style
- [ ] Escaped brackets: `[[` produces literal `[`, `]]` produces literal `]`
- [ ] `Markup.escape` prevents injection of style tags from dynamic content
- [ ] `Markup.stripTags` removes all tags, preserving text content
- [ ] `Markup.displayWidth` returns correct cell width ignoring tags
- [ ] Error reporting: unmatched tags produce clear MarkupError with position
- [ ] Lenient mode: `Markup.parseOrPlain` treats errors as plain text
- [ ] Integration: `markup` element renders styled text via segment pipeline
- [ ] Word wrapping preserves styles across line breaks
- [ ] No `System.*` imports in parser or style parsing code
- [ ] Parser handles empty tags, empty content, adjacent tags correctly
