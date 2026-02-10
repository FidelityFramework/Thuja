# PRD-025: JSON Syntax Highlighting

**Status:** Draft
**Tier:** 2 — Widget Catalog
**Dependencies:** PRD-001 (Segment), PRD-002 (Color)
**Unlocks:** API response display, config file viewers, data inspection tools

---

## 1. Problem Statement

JSON is ubiquitous in developer tools — API responses, configuration files, data inspection. Displaying raw JSON in a terminal is hard to read. Syntax-highlighted JSON with colored keys, strings, numbers, booleans, and structural characters is much more legible. Rich and Spectre.Console both provide JSON syntax highlighting widgets.

## 2. Reference Analysis

### Rich / Spectre.Console

Customizable colors per JSON element type:
- Braces `{}` — structural
- Brackets `[]` — structural
- Keys — distinct from values
- Strings — quoted values
- Numbers — numeric literals
- Booleans — `true`/`false`
- Null — `null`
- Colons and commas — punctuation

Rich additionally supports pretty-printing with configurable indentation.

## 3. F# Native Design

### JSON Tokenizer

```fsharp
/// JSON token types for syntax highlighting.
type JsonToken =
    | JsonBrace of string           // { or }
    | JsonBracket of string         // [ or ]
    | JsonKey of string             // "key" (before colon)
    | JsonString of string          // "value" (after colon, or in array)
    | JsonNumber of string          // 42, 3.14, -1e10
    | JsonBool of string            // true, false
    | JsonNull                      // null
    | JsonColon                     // :
    | JsonComma                     // ,
    | JsonWhitespace of string      // spaces, newlines, indentation

module JsonTokenizer =
    /// Tokenize a JSON string into typed tokens.
    /// This is a lexer, not a parser — it doesn't validate JSON structure.
    let tokenize (json: string) : JsonToken list = ...

    /// Pretty-print (reformat with indentation) then tokenize.
    let prettyTokenize (indent: int) (json: string) : JsonToken list = ...
```

### Style Map

```fsharp
/// Style configuration for JSON elements.
type JsonStyle = {
    Brace: Style
    Bracket: Style
    Key: Style
    String: Style
    Number: Style
    Bool: Style
    Null: Style
    Colon: Style
    Comma: Style
}

module JsonStyle =
    let defaults = {
        Brace = Style.fg (Named BrightWhite)
        Bracket = Style.fg (Named BrightWhite)
        Key = Style.fg (Named Cyan) |> Style.withAttr Bold
        String = Style.fg (Named Green)
        Number = Style.fg (Named Yellow)
        Bool = Style.fg (Named Magenta)
        Null = Style.fg (Named Red) |> Style.withAttr Italic
        Colon = Style.fg (Named BrightBlack)
        Comma = Style.fg (Named BrightBlack)
    }
```

### JSON Text Element

```fsharp
type JsonTextProps =
    | JsonColors of JsonStyle
    | Indent of int              // Pretty-print indent width (default: 2)
    | Compact                    // Don't pretty-print

/// Render syntax-highlighted JSON.
let jsonText (props: JsonTextProps list) (json: string) : Region -> ViewTree = ...
```

### Rendering

Tokens are mapped to styled segments:

```fsharp
let tokenToSegment (style: JsonStyle) (token: JsonToken) : Segment =
    match token with
    | JsonBrace s -> Text (s, ValueSome style.Brace)
    | JsonBracket s -> Text (s, ValueSome style.Bracket)
    | JsonKey s -> Text (s, ValueSome style.Key)
    | JsonString s -> Text (s, ValueSome style.String)
    | JsonNumber s -> Text (s, ValueSome style.Number)
    | JsonBool s -> Text (s, ValueSome style.Bool)
    | JsonNull -> Text ("null", ValueSome style.Null)
    | JsonColon -> Text (":", ValueSome style.Colon)
    | JsonComma -> Text (",", ValueSome style.Comma)
    | JsonWhitespace s -> Text (s, ValueNone)
```

## 4. Integration with Thuja Core

JsonText renders via the segment model. Tokens produce styled segments which are shaped to the Region via `Lines.setShape`. The element implements `IMeasurable` based on the pretty-printed width.

## 5. Responsive Behavior

- Pretty-printed JSON has a known width (indentation + content)
- On narrow terminals, lines clip (JSON doesn't wrap well)
- Measurement: minimum = widest key-value pair, maximum = widest pretty-printed line

## 6. API Surface

```fsharp
// Default styling, pretty-printed
jsonText [] """{"name": "Claude", "version": 4, "active": true}"""

// Custom styling
jsonText [ JsonColors { JsonStyle.defaults with Key = Style.fg (Named Yellow) } ]
    """{"data": [1, 2, 3], "status": null}"""

// Compact (no pretty-printing)
jsonText [ Compact ] jsonString
```

## 7. .NET-Free Design Notes

- JSON tokenizer is hand-written character scanning — no `System.Text.Json`
- No JSON parsing/validation (just lexing for coloring)
- Token types are a plain DU
- Style map is a record of Style values
- All rendering is pure: `string -> JsonToken list -> Segment list`

## 8. Acceptance Criteria

- [ ] JSON tokenizer correctly identifies: braces, brackets, keys, strings, numbers, bools, null
- [ ] Pretty-printing with configurable indentation
- [ ] Compact mode (no reformatting)
- [ ] Default color scheme visually distinguishes all element types
- [ ] Custom color configuration via JsonStyle
- [ ] Handles nested objects and arrays
- [ ] Handles escaped characters in strings
- [ ] IMeasurable implementation
- [ ] No `System.Text.Json` or external JSON library dependency
- [ ] Sample: API response display
