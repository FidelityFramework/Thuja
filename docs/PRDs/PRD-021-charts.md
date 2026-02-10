# PRD-021: Chart Widgets (Bar Chart, Breakdown Chart)

**Status:** Draft
**Tier:** 2, Widget Catalog
**Dependencies:** PRD-000 (Measure), PRD-001 (Segment), PRD-002 (Color)
**Unlocks:** Data visualization in terminal dashboards, monitoring tools, reports

---

## 1. Problem Statement

Terminal data visualization is increasingly common in CLI tools (system monitors, build outputs, analytics dashboards). Thuja has no charting capability. Rich and Spectre.Console both provide bar charts and breakdown (proportional) charts.

## 2. Reference Analysis

### Rich (Python)

**Bar Chart:**
- Horizontal bars with labels, values, and colors
- Auto-scaling to max value or explicit max
- Value display (inline or suppressed)
- Label alignment (left, center, right)

**Breakdown Chart:**
- Proportional horizontal bar (like a stacked bar chart in one row)
- Each segment colored and labeled
- Legend with labels, values, and optional percentages
- Compact mode (bar only) or full mode (bar + legend)

### Spectre.Console (C#)

Same two chart types. BarChart adds `UseValueFormatter` for custom formatting (currency, percentages). BreakdownChart adds `ShowPercentage()`, compact/full modes.

## 3. F# Native Design

### Bar Chart

```fsharp
/// A single bar in a bar chart.
type BarItem = {
    Label: string
    Value: float
    Color: Color
}

/// Properties for bar chart rendering.
type BarChartProps =
    | MaxValue of float         // Fixed scale max (default: auto from data)
    | ShowValues                // Display value labels on bars
    | ValueFormat of (float -> string)  // Custom value formatter
    | LabelWidth of int         // Fixed label column width
    | BarChar of string         // Character for filled portion (default: "█")

/// Render a horizontal bar chart.
let barChart (props: BarChartProps list) (items: BarItem list) : Region -> ViewTree = ...
```

**Rendering:**
```
Label A  ████████████████████░░░░  80
Label B  ██████████████████████████ 100
Label C  ████████░░░░░░░░░░░░░░░░  32
```

Each row: `[label] [filled bar][empty bar] [value]`
- Label column: left-aligned, fixed width (auto or explicit)
- Bar column: fills remaining space (measurement-aware)
- Value column: right-aligned, fixed width
- Fill ratio: `value / maxValue * barWidth`

### Breakdown Chart

```fsharp
/// A segment in a breakdown chart.
type BreakdownItem = {
    Label: string
    Value: float
    Color: Color
}

/// Properties for breakdown chart rendering.
type BreakdownChartProps =
    | ShowPercentage            // Show % in legend
    | ShowValues                // Show values in legend
    | ShowTags                  // Show legend (default: true)
    | Compact                   // Bar only, no legend
    | BarChar of string         // Fill character (default: "█")

/// Render a proportional breakdown chart.
let breakdownChart (props: BreakdownChartProps list) (items: BreakdownItem list) : Region -> ViewTree = ...
```

**Rendering (full mode):**
```
████████████████░░░░░░████████

 ● Label A 45.2%  ● Label B 22.1%  ● Label C 32.7%
```

**Rendering (compact):**
```
████████████████░░░░░░████████
```

Each segment's width = `value / totalValue * barWidth`. Legend items show colored bullet + label + optional value/percentage.

## 4. Integration with Thuja Core

Charts implement `IElement` and optionally `IMeasurable`:
- BarChart minimum width = label_width + 10 (minimal bar) + value_width
- BarChart maximum width = unbounded (bar fills space)
- BreakdownChart minimum width = number_of_items (1 char per segment)
- BreakdownChart maximum width = unbounded

Charts render via the segment model (PRD-001) for proper styling and composition within panels, layouts, and other containers.

## 5. Responsive Behavior

- Bar width scales linearly with available width
- On very narrow terminals, value labels can be hidden
- BreakdownChart legend wraps to multiple lines or hides below threshold
- Minimum: each bar segment gets at least 1 character

## 6. API Surface

```fsharp
// Bar chart
barChart [ ShowValues; MaxValue 100.0 ] [
    { Label = "Downloads"; Value = 85.0; Color = Named Green }
    { Label = "Uploads"; Value = 42.0; Color = Named Blue }
    { Label = "Errors"; Value = 3.0; Color = Named Red }
]

// Breakdown chart with legend
breakdownChart [ ShowPercentage ] [
    { Label = "Rust"; Value = 45.0; Color = Named Red }
    { Label = "F#"; Value = 30.0; Color = Named Cyan }
    { Label = "Python"; Value = 25.0; Color = Named Yellow }
]
```

## 7. .NET-Free Design Notes

- `BarItem` and `BreakdownItem` are plain records
- All calculations are pure arithmetic: `value / max * width`
- Value formatting is a user-provided function `float -> string`, avoiding any `System.Globalization` dependency
- Color assignment uses the Color type (PRD-002), resolved at render time
- No `System.Math` beyond basic arithmetic available in F# core

## 8. Acceptance Criteria

- [ ] `barChart` renders horizontal bars with labels, bars, and values
- [ ] Auto-scaling max value from data
- [ ] Custom value formatting via function parameter
- [ ] `breakdownChart` renders proportional segments with colored fills
- [ ] Legend with colored bullets, labels, optional values/percentages
- [ ] Compact mode (bar only, no legend)
- [ ] Bars scale responsively with available width
- [ ] IMeasurable implementation for both chart types
- [ ] Sample app: system resource dashboard with charts
- [ ] No BCL dependencies in chart code
