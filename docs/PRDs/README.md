# Thuja → Fidelity TUI: PRD Roadmap

## Vision

Build a Rich-level terminal UI framework in idiomatic F# 'native', using Thuja's Elm architecture and ViewTree diffing as the foundation. The target is a fully .NET-free TUI library that runs natively on the Fidelity Framework.

## Architecture Principles

1. **Rich (Python) is the primary reference**. Rich has the cleaner protocol design. Spectre.Console is a C# port of Rich with fewer features and more OOP ceremony.

2. **Keep Thuja's superior core.** ViewTree structural diffing, Region-based spatial layout, and Elm MVU are architecturally stronger than Rich's full-redraw model. Build Rich's widget richness on Thuja's foundation.

3. **No .NET CLR/BCL assumptions.** All core types are pure F# (DUs, records, modules, functions). No `System.Threading`, `System.Console`, `System.DateTime`, `System.Text.RegularExpressions`, or other BCL dependencies in core code. Runtime-specific behavior (timers, terminal IO, environment access) is injected through abstract interfaces.

4. **Firefly-ready concurrency.** The Elm command/subscription model abstracts concurrency. Timer ticks, async effects, and IO are all expressed as messages. The runtime adapter (currently .NET, eventually Firefly) provides the scheduling.

5. **Responsive-first layout.** Tiling window managers (Hyprland/Omarchy) resize terminals aggressively. The Measure protocol and responsive layout system make content adaptation a first-class concern, not an afterthought.

## Dependency Graph

```
Tier 0: Foundation
  PRD-000 Measure Protocol ──────────────┐
  PRD-001 Segment Model ─────────────────┤
  PRD-002 Color System ──────────────────┤
                                         │
Tier 1: Core Capabilities                │
  PRD-010 Markup Text ──── (001, 002) ───┤
  PRD-011 Responsive Layout (000, 001) ──┤
  PRD-012 Live Rendering ── (001, 002) ──┤
  PRD-013 Progress System ─ (000-012) ───┤
  PRD-014 Border Expansion (independent) ┤
                                         │
Tier 2: Widget Catalog                   │
  PRD-020 Tree Widget ──── (000,001,010,014)
  PRD-021 Charts ────────── (000,001,002)
  PRD-022 FigletText ────── (000,001,002)
  PRD-023 Canvas ─────────── (001,002)
  PRD-024 Markdown ───────── (001,010,014,020)
  PRD-025 JSON Text ──────── (001,002)
  PRD-026 Prompts ─────────── (001,010,012)
  PRD-027 Calendar ────────── (000,001,014)
```

## PRD Index

### Tier 0: Foundation

| PRD | Title | Status | Summary |
|-----|-------|--------|---------|
| [000](PRD-000-measure-protocol.md) | Measure Protocol | Draft | Min/max width negotiation for responsive layout |
| [001](PRD-001-segment-model.md) | Segment Model | Draft | Composable intermediate rendering representation |
| [002](PRD-002-color-system.md) | Color System | Draft | Capability detection and automatic color downgrading |

### Tier 1: Core Capabilities

| PRD | Title | Status | Summary |
|-----|-------|--------|---------|
| [010](PRD-010-markup-text.md) | Markup Text | Draft | Inline style syntax `[bold red]text[/]` |
| [011](PRD-011-responsive-layout.md) | Responsive Layout | Draft | Auto/Fr/Fixed sizing, breakpoints, collapse |
| [012](PRD-012-live-rendering.md) | Live Rendering | Draft | Animation clock, spinners, status display |
| [013](PRD-013-progress-system.md) | Progress System | Draft | Composable progress bars with task tracking |
| [014](PRD-014-border-expansion.md) | Border Expansion | Draft | 18+ border styles for panels and tables |

### Tier 2: Widget Catalog

| PRD | Title | Status | Summary |
|-----|-------|--------|---------|
| [020](PRD-020-tree-widget.md) | Tree Widget | Draft | Hierarchical data with guide lines |
| [021](PRD-021-charts.md) | Charts | Draft | Bar charts and breakdown charts |
| [022](PRD-022-figlet-text.md) | FigletText | Draft | ASCII art banners from FIGlet fonts |
| [023](PRD-023-canvas.md) | Canvas | Draft | Pixel-level drawing with half-blocks |
| [024](PRD-024-markdown-rendering.md) | Markdown | Draft | CommonMark rendering for terminal |
| [025](PRD-025-json-text.md) | JSON Text | Draft | Syntax-highlighted JSON display |
| [026](PRD-026-prompts.md) | Prompts | Draft | Text input, selection, multi-select, confirmation |
| [027](PRD-027-calendar.md) | Calendar | Draft | Monthly calendar with event highlighting |

## .NET Boundary Rules

Every PRD follows these rules:

- **Core types** (in `src/Thuja/`): Pure F#. No `System.*` imports. No `open System`.
- **Runtime effects** (timers, IO, environment): Injected via abstract interfaces. The current .NET implementation provides these; Firefly will provide alternatives.
- **String processing**: Hand-written parsers. No `System.Text.RegularExpressions`.
- **Date/time**: Pure integer arithmetic. No `System.DateTime`.
- **Collections**: F# `list`, `Map`, `Set`, `array`. No `System.Collections.*`.
- **Concurrency**: Elm commands and subscriptions. No `System.Threading` in core.

The backend (`src/Thuja.Tutu/`) is explicitly the .NET boundary. It uses BCL types to talk to the terminal. When migrating to Fidelity/Firefly, only the backend changes.
