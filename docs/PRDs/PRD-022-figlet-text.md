# PRD-022: FigletText (ASCII Art Banners)

**Status:** Draft
**Tier:** 2 — Widget Catalog
**Dependencies:** PRD-000 (Measure), PRD-001 (Segment), PRD-002 (Color)
**Unlocks:** Application splash screens, section headers, visual emphasis

---

## 1. Problem Statement

Large ASCII art text banners are a staple of terminal applications — splash screens, section headers, tool branding. FIGlet is the standard format for ASCII art fonts, with hundreds of freely available font files. Both Rich and Spectre.Console support FIGlet rendering.

## 2. Reference Analysis

### Rich / Spectre.Console

- Render text using FIGlet font files (`.flf` format)
- Alignment: left, center, right
- Color and style applied to the ASCII art output
- Spectre.Console includes a default font; Rich includes "standard" FIGlet font
- FIGlet format: each character is defined as multiple lines of ASCII art

### FIGlet Format (.flf)

FIGlet fonts are plain text files defining character art:
- Header line with metadata (height, baseline, comment lines, etc.)
- Each printable ASCII character (32-126) defined as N lines of art
- Special markers (`@`, `@@`) delimit character boundaries
- Characters are composed horizontally with optional smushing/kerning rules

## 3. F# Native Design

### Font Parser

```fsharp
/// A parsed FIGlet font.
type FigletFont = {
    Height: int                                // Lines per character
    Baseline: int                              // Baseline offset
    Characters: Map<char, string list>         // char -> lines of art
    SmushMode: int                             // Smushing/kerning rules
}

module FigletFont =
    /// Parse a FIGlet font from its text content.
    /// Takes the font content as a string to avoid file IO dependency.
    let parse (content: string) : Result<FigletFont, string> = ...

    /// The default built-in font (embedded as a string constant).
    let defaultFont : FigletFont = ...

    /// Render a string using the font.
    let render (font: FigletFont) (text: string) : string list =
        // For each line of font height:
        //   Concatenate the corresponding line of each character
        // Apply smushing rules for character adjacency
        ...
```

### FigletText Element

```fsharp
type FigletProps =
    | Font of FigletFont
    | FigletColor of Color
    | FigletAlign of Align      // Left, Center, Right

/// Render text as a large ASCII art banner.
let figlet (props: FigletProps list) (text: string) : Region -> ViewTree = ...
```

### Measurement

```fsharp
// IMeasurable:
// Maximum = sum of character widths (widest line of each character)
// Minimum = widest single character (can't break mid-character)
```

## 4. Integration with Thuja Core

FigletText renders as a multi-line text element via the segment model. Each line of the ASCII art becomes a styled segment. The element implements `IMeasurable` so layout containers can allocate appropriate space.

## 5. Responsive Behavior

- FIGlet art has a fixed character width per font — it can't reflow
- On narrow terminals: left-aligned with clipping (art extends off-screen)
- With Center alignment: centered within available width, clipped if too wide
- Measurement minimum = widest single character — below this, output is meaningless

## 6. API Surface

```fsharp
// Default font, centered
figlet [ FigletAlign Center; FigletColor (Named Cyan) ] "HELLO"

// Custom font (loaded by user from file content)
let customFont = FigletFont.parse fontFileContent
figlet [ Font customFont ] "WELCOME"
```

## 7. .NET-Free Design Notes

- Font parsing is pure: `string -> Result<FigletFont, string>`
- No file IO in the library — user provides font content as string
- Character map is `Map<char, string list>` — F# immutable map
- Rendering is pure string concatenation
- Default font embedded as a string literal in source (no resource files)
- No `System.IO`, `System.Resources`, or reflection

## 8. Acceptance Criteria

- [ ] FIGlet font parser handles standard `.flf` format
- [ ] Default font embedded and available without file IO
- [ ] Text rendered correctly (horizontal character composition)
- [ ] Basic smushing/kerning support
- [ ] Alignment: left, center, right
- [ ] Color and style applied to output
- [ ] `IMeasurable` implementation
- [ ] Clipping on narrow terminals (no garbled output)
- [ ] Sample app: splash screen banner
- [ ] No file IO or resource loading in library code
