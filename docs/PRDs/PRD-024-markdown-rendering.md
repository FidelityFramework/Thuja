# PRD-024: Markdown Rendering

**Status:** Draft
**Tier:** 2, Widget Catalog
**Dependencies:** PRD-001 (Segment), PRD-010 (Markup), PRD-014 (Borders), PRD-020 (Tree)
**Unlocks:** Help text display, README rendering, chat message formatting, documentation

---

## 1. Problem Statement

Markdown is the lingua franca of developer documentation and AI chat output. Terminal applications frequently need to render Markdown content: help text, chat messages, README display, and API response formatting. Rich provides a Markdown renderer that handles headings, code blocks, lists, tables, emphasis, and links. This is particularly relevant for AI terminal experiences where model output is typically Markdown.

## 2. Reference Analysis

### Rich (Python)

- Uses `markdown-it-py` for parsing (CommonMark compliant)
- Visitor pattern: token stream mapped to `MarkdownElement` subclasses
- Headings: styled rules with text
- Code blocks: syntax-highlighted via Pygments (rendered as Panel with Syntax inside)
- Inline code: styled with backtick rendering
- Lists: bullet and numbered, nested with indentation
- Block quotes: indented with styled left border
- Tables: rendered as Rich Tables
- Emphasis: bold, italic mapped to terminal attributes
- Links: displayed as `[text](url)` with link style
- Horizontal rules: rendered as Rich Rule elements
- Paragraphs: word-wrapped text with blank line separation

### Spectre.Console

No built-in Markdown renderer (gap in Spectre.Console vs Rich).

## 3. F# Native Design

### Markdown AST

Rather than depending on an external Markdown parser, define a simple AST that covers the CommonMark elements needed for terminal rendering:

```fsharp
/// Inline Markdown elements.
type MdInline =
    | MdText of string
    | MdBold of MdInline list
    | MdItalic of MdInline list
    | MdCode of string                          // `inline code`
    | MdLink of text: MdInline list * url: string
    | MdSoftBreak
    | MdHardBreak

/// Block Markdown elements.
type MdBlock =
    | MdParagraph of MdInline list
    | MdHeading of level: int * MdInline list   // # to ######
    | MdCodeBlock of language: string option * code: string
    | MdBlockQuote of MdBlock list
    | MdBulletList of MdBlock list list          // list of items, each item is blocks
    | MdOrderedList of start: int * MdBlock list list
    | MdTable of headers: MdInline list list * rows: MdInline list list list
    | MdHorizontalRule
    | MdThematicBreak

/// A Markdown document.
type MdDocument = MdBlock list
```

### Parser

```fsharp
module MarkdownParser =
    /// Parse a Markdown string into an AST.
    /// Supports CommonMark subset: headings, emphasis, code, lists, quotes, tables.
    let parse (input: string) : MdDocument = ...
```

The parser is a hand-written recursive descent parser (no external dependency). It handles the CommonMark subset relevant to terminal rendering. The goal is not full spec compliance, but coverage sufficient for documentation and AI output.

### Renderer

```fsharp
module MarkdownRenderer =
    /// Render a Markdown document to view tree elements.
    let render (props: MarkdownProps list) (doc: MdDocument) : Region -> ViewTree = ...

type MarkdownProps =
    | HeadingStyle of (int -> Style)    // Level -> style
    | CodeBlockBorder of BoxBorder      // Border style for code blocks
    | CodeBlockStyle of Style           // Style for code block content
    | QuoteStyle of Style               // Block quote style
    | QuoteBorder of string             // Block quote left border char (default "▌")
    | LinkStyle of Style                // Link rendering style
    | BulletChars of string list        // Bullet characters per nesting level
```

### Rendering Rules

```fsharp
// Headings: styled text with rule below (levels 1-2) or just styled text
// # Heading 1 → bold + colored + underline rule
// ## Heading 2 → bold + colored
// ### Heading 3+ → bold

// Code blocks: Panel with syntax-highlighted content
// ```fsharp      ┌─────────────────────┐
// let x = 42  → │ let x = 42          │
// ```            └─────────────────────┘

// Block quotes: indented with colored left border
// > quoted    → ▌ quoted text here
// > text      → ▌ text continued

// Lists:
// - item 1   → • item 1
// - item 2   →  • item 2
//   - nested →    ◦ nested

// Emphasis:
// **bold**   → bold attribute
// *italic*   → italic attribute
// `code`     → distinct style (background highlight)

// Links:
// [text](url) → styled text (url shown if different from text)

// Horizontal rules:
// ---         → Rule element spanning width

// Tables:
// | H1 | H2 | → Table element with headers and rows
// |----|----|
// | A  | B  |
```

## 4. Integration with Thuja Core

The Markdown renderer produces a composite view function that stacks block elements vertically using `rows`. Each block is rendered as the appropriate Thuja element (text, panel, rule, table, etc.). This means Markdown inherits all the responsive behavior of the underlying elements.

```fsharp
/// Top-level convenience function.
let markdown (props: MarkdownProps list) (content: string) : Region -> ViewTree =
    let doc = MarkdownParser.parse content
    MarkdownRenderer.render props doc
```

## 5. Responsive Behavior

- Text paragraphs word-wrap naturally (Text element behavior)
- Code blocks clip horizontally (code doesn't wrap)
- Tables use measurement-aware column sizing (PRD-000)
- Lists maintain indent but label text wraps
- Headings truncate with ellipsis on narrow terminals

## 6. API Surface

```fsharp
// Simple Markdown rendering
markdown [] """
# Hello World

This is a **Markdown** document rendered in the terminal.

## Features

- Headings
- *Emphasis* and **bold**
- `inline code`
- Code blocks
- Lists and tables

```fsharp
let x = 42
printfn "%d" x
```
"""

// Styled Markdown
markdown [
    HeadingStyle (fun level ->
        match level with
        | 1 -> Style.fg (Named Cyan) |> Style.withAttr Bold
        | 2 -> Style.fg (Named Blue) |> Style.withAttr Bold
        | _ -> Style.withAttr Bold Style.default')
    CodeBlockBorder BoxBorders.rounded
    CodeBlockStyle (Style.fg (Named Green))
] content
```

## 7. .NET-Free Design Notes

- Markdown AST types are plain DUs with no external parser dependency
- Hand-written parser: no `System.Text.RegularExpressions`
- Renderer composes existing Thuja elements (text, panel, rule, table, rows)
- No `System.IO` for reading files. The user provides Markdown as a string
- Syntax highlighting for code blocks is optional and a separate concern (can be added later as a pure tokenizer → styled spans function)

### Parser Approach

A hand-written CommonMark-subset parser avoids external dependencies. Priority order:

1. Block structure (headings, code blocks, lists, quotes, paragraphs)
2. Inline parsing (emphasis, code, links)
3. Table extension (GFM-style)

This is a meaningful parser (~300-500 lines) but well-scoped for the subset needed.

## 8. Acceptance Criteria

- [ ] Markdown parser handles: headings (1-6), paragraphs, emphasis (bold/italic)
- [ ] Inline code with distinct styling
- [ ] Fenced code blocks with language annotation
- [ ] Bullet lists (nested)
- [ ] Ordered lists (nested)
- [ ] Block quotes with styled border
- [ ] Horizontal rules
- [ ] Links with URL display
- [ ] GFM-style tables (header + rows)
- [ ] Renderer produces view tree from parsed AST
- [ ] Styling customizable via MarkdownProps
- [ ] Word wrapping in paragraphs and list items
- [ ] Sample app: README display or chat message rendering
- [ ] No external parser dependency (hand-written)
- [ ] No `System.Text.RegularExpressions` import
