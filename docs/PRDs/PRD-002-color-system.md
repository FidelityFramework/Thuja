# PRD-002: Color System and Terminal Capability Detection

**Status:** Draft
**Tier:** 0, Foundation
**Dependencies:** None (parallel with PRD-000, PRD-001)
**Unlocks:** PRD-010, PRD-012, all widget rendering with reliable color output

---

## 1. Problem Statement

Thuja's current color model defines `Color = Reset | Ansi of byte | Rgb of R * G * B` and passes colors directly to the backend. There is no:

- **Capability detection:** No way to know if the terminal supports 24-bit, 256, 16, or 8 colors. Rgb colors sent to a 16-color terminal produce garbage.
- **Downgrading:** No automatic mapping from TrueColor to 256-color to 16-color when the terminal doesn't support the requested depth.
- **Named color palette:** No way to reference "Red" and have it resolve to the appropriate representation for the terminal's capability.
- **Theme system:** No way to define a cohesive color palette that adapts to terminal capabilities and user preferences (light/dark).

For a framework targeting diverse terminal environments (Kitty, Ghostty, Alacritty, tmux, SSH sessions, CI environments), robust color handling is essential.

## 2. Reference Analysis

### Rich (Python)

Rich detects color depth via environment variables:
- `NO_COLOR` env var → no colors (standard: https://no-color.org)
- `COLORTERM=truecolor` or `COLORTERM=24bit` → 24-bit
- Terminal-specific detection (Kitty, iTerm2, Windows Terminal)
- `TERM` containing `256color` → 256 colors
- Fallback: 8 colors (standard ANSI)

Downgrading chain: TrueColor (16.7M) → 256-color → 16-color → no color.

The downgrade algorithm converts RGB to the nearest color in the target palette using perceptual distance (weighted Euclidean in RGB space or CIE LAB).

Rich defines 256 named colors (CSS color names) that map to specific RGB values and include pre-computed nearest-match for 256-color and 16-color palettes.

### Spectre.Console (C#)

Same model as Rich. `ColorSystem` enum: `NoColors`, `Legacy` (3-bit/8), `Standard` (4-bit/16), `EightBit` (256), `TrueColor` (24-bit). Auto-detects from environment.

### Thuja (Current)

```fsharp
type Color = Reset | Ansi of byte | Rgb of R: byte * G: byte * B: byte
```

No detection, no downgrading. `Color.Hex` parses hex strings to Rgb. Named colors (Red, Blue, etc.) are defined as `Ansi` values. No mapping between Rgb and Ansi.

## 3. F# Native Design

### Color Depth

```fsharp
/// Terminal color capability levels.
type ColorDepth =
    | NoColor       // Monochrome / NO_COLOR set
    | Basic         // 8 standard ANSI colors (3-bit: 0-7)
    | Standard      // 16 colors (4-bit: 0-15, includes bright variants)
    | EightBit      // 256 colors (8-bit: 0-255)
    | TrueColor     // 16.7M colors (24-bit RGB)
```

### Enhanced Color Type

```fsharp
/// A color specification that can be resolved for any terminal capability.
type Color =
    | Default                               // Terminal default (reset)
    | Named of name: ColorName              // Named color (from palette)
    | Ansi of code: byte                    // Direct ANSI code (0-255)
    | Rgb of r: byte * g: byte * b: byte    // Direct 24-bit RGB
    | Hex of hex: string                    // Hex string (parsed to RGB)

/// Standard named colors with known ANSI and RGB mappings.
type ColorName =
    | Black | Red | Green | Yellow | Blue | Magenta | Cyan | White
    | BrightBlack | BrightRed | BrightGreen | BrightYellow
    | BrightBlue | BrightMagenta | BrightCyan | BrightWhite
    // Extended named colors can be added (CSS color names, etc.)
```

### Color Resolution

```fsharp
module ColorResolver =
    /// Resolve a Color to a concrete ANSI code or RGB value for a given depth.
    let resolve (depth: ColorDepth) (color: Color) : ResolvedColor =
        match color, depth with
        | Default, _ -> ResolvedDefault
        | _, NoColor -> ResolvedDefault  // strip all color
        | Named name, _ -> resolveNamed depth name
        | Ansi code, TrueColor -> ResolvedAnsi code
        | Ansi code, EightBit -> ResolvedAnsi code
        | Ansi code, Standard -> ResolvedAnsi (downgradeAnsiTo16 code)
        | Ansi code, Basic -> ResolvedAnsi (downgradeAnsiTo8 code)
        | Rgb (r, g, b), TrueColor -> ResolvedRgb (r, g, b)
        | Rgb (r, g, b), EightBit -> ResolvedAnsi (rgbTo256 r g b)
        | Rgb (r, g, b), Standard -> ResolvedAnsi (rgbTo16 r g b)
        | Rgb (r, g, b), Basic -> ResolvedAnsi (rgbTo8 r g b)
        | Hex hex, depth -> resolve depth (parseHex hex)

    /// The output of color resolution, ready for terminal encoding.
    type ResolvedColor =
        | ResolvedDefault
        | ResolvedAnsi of byte
        | ResolvedRgb of byte * byte * byte
```

### Capability Detection

```fsharp
module TerminalCapability =
    /// Detect color depth from environment.
    /// Takes an environment variable reader function to avoid System.Environment dependency.
    let detectColorDepth (getEnv: string -> string option) : ColorDepth =
        // NO_COLOR takes absolute precedence (https://no-color.org)
        match getEnv "NO_COLOR" with
        | Some _ -> NoColor
        | None ->
        // Check for explicit 24-bit
        match getEnv "COLORTERM" with
        | Some "truecolor" | Some "24bit" -> TrueColor
        | _ ->
        // Check TERM for 256color
        match getEnv "TERM" with
        | Some t when t.Contains "256color" -> EightBit
        | Some "dumb" -> NoColor
        | _ ->
        // Check terminal-specific vars
        match getEnv "KITTY_WINDOW_ID", getEnv "GHOSTTY_RESOURCES_DIR" with
        | Some _, _ | _, Some _ -> TrueColor  // Kitty and Ghostty support TrueColor
        | _ ->
        // Check WT_SESSION (Windows Terminal)
        match getEnv "WT_SESSION" with
        | Some _ -> TrueColor
        | None -> Standard  // Conservative default

    /// Detect Unicode support.
    let detectUnicode (getEnv: string -> string option) : bool =
        match getEnv "LANG", getEnv "LC_ALL" with
        | Some l, _ when l.Contains "UTF" -> true
        | _, Some l when l.Contains "UTF" -> true
        | _ -> false
```

### RGB to 256/16/8 Downgrade

```fsharp
module ColorDowngrade =
    /// The 256-color ANSI palette as RGB tuples (compile-time data).
    let private palette256 : (byte * byte * byte) array = [| ... |]

    /// The 16-color ANSI palette as RGB tuples.
    let private palette16 : (byte * byte * byte) array = [| ... |]

    /// Perceptual color distance (weighted Euclidean in RGB space).
    let private distance (r1, g1, b1) (r2, g2, b2) =
        let dr = float (int r1 - int r2)
        let dg = float (int g1 - int g2)
        let db = float (int b1 - int b2)
        // Weighted for human perception: green > red > blue
        2.0 * dr * dr + 4.0 * dg * dg + 3.0 * db * db

    /// Find nearest color in a palette.
    let private nearest (palette: (byte * byte * byte) array) (r, g, b) : byte =
        palette
        |> Array.mapi (fun i rgb -> (i, distance (r, g, b) rgb))
        |> Array.minBy snd
        |> fst
        |> byte

    let rgbTo256 r g b = nearest palette256 (r, g, b)
    let rgbTo16 r g b = nearest palette16 (r, g, b)
    let rgbTo8 r g b = nearest palette16.[0..7] (r, g, b)
```

## 4. Integration with Thuja Core

### Terminal Profile

A new `TerminalProfile` record captures detected capabilities:

```fsharp
type TerminalProfile = {
    ColorDepth: ColorDepth
    Unicode: bool
    Width: int
    Height: int
}
```

This is created once at backend initialization and passed through the rendering context. It replaces ad-hoc terminal size queries.

### Backend Changes

The backend resolves colors at command execution time:

```fsharp
// In Backend.Execute:
// Before sending PrintWith to terminal, resolve colors for terminal capability
let resolvedStyle = Style.resolve profile.ColorDepth style
```

This keeps all color resolution at the boundary (backend), not in element code. Elements freely use `Rgb`, `Named`, `Hex`, and the backend downgrades as needed.

### Style Resolution

```fsharp
module Style =
    /// Resolve a Style's colors for the terminal's capability.
    let resolve (depth: ColorDepth) (style: Style) : Style =
        { style with
            Foreground = ColorResolver.resolve depth style.Foreground
            Background = ColorResolver.resolve depth style.Background }
```

## 5. Responsive Behavior

Color depth doesn't change on resize, but the profile may need refresh when:
- Terminal is reconnected (SSH sessions)
- User changes terminal settings mid-session

The `TerminalProfile` should be re-queried on significant events, not cached forever.

## 6. API Surface

### For Users

```fsharp
// Named colors work everywhere -- auto-downgrade to terminal capability
text [ Color (Named Red) ] "Error message"
text [ Color (Rgb (255, 128, 0)) ] "Orange works on TrueColor, nearest-match elsewhere"
text [ Color (Hex "#FF8000") ] "Same orange via hex"

// Explicit theme-aware styling
let warning = Named Yellow
let error = Named Red
let info = Named Cyan
```

### For Backend Implementors

```fsharp
// Backend receives resolved colors -- no need to handle downgrading
type IBackend =
    abstract Profile: TerminalProfile
    abstract Execute: Command list -> unit
    // ...
```

## 7. .NET-Free Design Notes

- `TerminalCapability.detectColorDepth` takes `getEnv: string -> string option` with no `System.Environment` dependency. The caller provides environment access.
- Color palettes are compile-time data arrays with no file IO or resource loading
- Distance calculations are pure arithmetic with no `System.Math` needed (just float ops)
- `ColorName`, `ColorDepth`, `Color` are all plain DUs
- `TerminalProfile` is a plain record
- No `System.Drawing.Color`, no `System.ConsoleColor`
- All functions are pure: `Color * ColorDepth -> ResolvedColor`

## 8. Acceptance Criteria

- [ ] `ColorDepth` type defined with all five levels
- [ ] `Color` type extended with `Named`, `Hex` cases
- [ ] `ColorName` DU covering at minimum the 16 standard ANSI names
- [ ] `TerminalCapability.detectColorDepth` implemented with env var detection
- [ ] `ColorResolver.resolve` correctly downgrades for all depth levels
- [ ] RGB-to-256 nearest-match uses perceptual distance weighting
- [ ] RGB-to-16 nearest-match tested against known color samples
- [ ] `NO_COLOR` environment variable respected (https://no-color.org)
- [ ] `TerminalProfile` record defined and used in backend
- [ ] Existing `Ansi of byte` color case continues to work unchanged
- [ ] No `System.Environment` or other BCL imports in core color code
- [ ] Color detection function is pure (injected `getEnv` dependency)
