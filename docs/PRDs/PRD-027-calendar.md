# PRD-027: Calendar Widget

**Status:** Draft
**Tier:** 2 — Widget Catalog
**Dependencies:** PRD-000 (Measure), PRD-001 (Segment), PRD-014 (Borders)
**Unlocks:** Date pickers, event displays, scheduling interfaces

---

## 1. Problem Statement

Calendar display is useful in scheduling tools, date pickers, event trackers, and dashboard widgets. Both Rich and Spectre.Console provide calendar widgets. A monthly calendar grid with event highlighting is a compact, useful component.

## 2. Reference Analysis

### Rich / Spectre.Console

- Monthly calendar grid display
- Date highlighting via events: `AddCalendarEvent(year, month, day)`
- `HighlightStyle` for event dates
- `HeaderStyle` for month/year title
- Multiple border styles (inherits from Table)
- `Culture` parameter for localization (week start day, month names)

Layout:
```
     December 2024
 Mo Tu We Th Fr Sa Su
                    1
  2  3  4  5  6  7  8
  9 10 11 12 13 14 15
 16 17 18 19 20 21 22
 23 24 25 26 27 28 29
 30 31
```

## 3. F# Native Design

### Calendar Data Model

```fsharp
/// A calendar event (highlighted date).
type CalendarEvent = {
    Year: int
    Month: int
    Day: int
    Style: Style option     // Custom style for this date (overrides default highlight)
}

/// Calendar display configuration.
type CalendarProps =
    | HeaderStyle of Style
    | HighlightStyle of Style
    | DayStyle of Style
    | WeekendStyle of Style
    | TodayStyle of Style
    | WeekStart of DayOfWeek    // Monday or Sunday
    | Border of BoxBorder

/// Days of the week (no System.DayOfWeek dependency).
type DayOfWeek = Monday | Tuesday | Wednesday | Thursday | Friday | Saturday | Sunday
```

### Date Calculation

```fsharp
module DateCalc =
    /// Days in a month (handles leap years).
    let daysInMonth (year: int) (month: int) : int =
        match month with
        | 2 ->
            let leap = (year % 4 = 0 && year % 100 <> 0) || (year % 400 = 0)
            if leap then 29 else 28
        | 4 | 6 | 9 | 11 -> 30
        | _ -> 31

    /// Day of week for a given date (Zeller's congruence or similar).
    /// Returns 0 = Monday, 6 = Sunday.
    let dayOfWeek (year: int) (month: int) (day: int) : int =
        // Tomohiko Sakamoto's algorithm (compact, no lookup tables)
        let t = [| 0; 3; 2; 5; 0; 3; 5; 1; 4; 6; 2; 4 |]
        let y = if month < 3 then year - 1 else year
        (y + y/4 - y/100 + y/400 + t.[month - 1] + day) % 7
        // Returns 0=Sunday, adjust to 0=Monday: (result + 6) % 7

    /// Generate the grid for a month: list of weeks, each week is 7 day slots.
    let monthGrid (year: int) (month: int) (weekStart: DayOfWeek) : int option list list =
        // Each slot is Some day or None (empty)
        ...

    /// Short day names.
    let dayNames (weekStart: DayOfWeek) : string list =
        let names = ["Mo"; "Tu"; "We"; "Th"; "Fr"; "Sa"; "Su"]
        let offset = match weekStart with Monday -> 0 | Sunday -> 6 | _ -> ...
        // Rotate list by offset
        ...

    /// Month names.
    let monthName (month: int) : string =
        [| "January"; "February"; "March"; "April"; "May"; "June"
           "July"; "August"; "September"; "October"; "November"; "December" |]
        |> Array.item (month - 1)
```

### Calendar Element

```fsharp
/// Render a monthly calendar.
let calendar
    (props: CalendarProps list)
    (year: int)
    (month: int)
    (events: CalendarEvent list)
    : Region -> ViewTree =
    fun region ->
        let weekStart = props |> findProp (function WeekStart ws -> Some ws | _ -> None) |> Option.defaultValue Monday
        let grid = DateCalc.monthGrid year month weekStart
        let eventSet = events |> List.map (fun e -> (e.Year, e.Month, e.Day, e.Style)) |> Set.ofList

        // Render:
        // 1. Header: "  December 2024" (centered)
        // 2. Day names row: "Mo Tu We Th Fr Sa Su"
        // 3. Week rows: " 1  2  3  4  5  6  7"
        //    - Highlighted dates get highlight style
        //    - Today gets today style
        //    - Weekends get weekend style
        ...
```

### Measurement

```fsharp
// Calendar has fixed dimensions:
// Width: 7 days * 3 chars + padding = ~22 characters minimum
// Height: header + day names + 5-6 week rows = ~8 lines
// IMeasurable: Minimum = 22, Maximum = 28 (with spacing)
```

## 4. Integration with Thuja Core

Calendar renders as a structured text element. Each cell (day number) is a styled segment. The grid is built using the segment model and shaped to the Region.

Can be composed in layouts:
```fsharp
columns [ LayoutSlot.auto; LayoutSlot.fr 1 ] [
    calendar [ HighlightStyle (Style.fg (Named Red)) ] 2024 12 events
    eventList  // details panel
]
```

## 5. Responsive Behavior

- Calendar has a natural minimum width (~22 chars) — below this, it clips
- If extra width available, cells can expand with more padding
- Height is fixed (header + 7 rows max) — doesn't need vertical responsiveness
- On very narrow terminals, the calendar could switch to compact mode (single column: "Dec 1 - Event name")

## 6. API Surface

```fsharp
// Basic calendar
calendar [] 2024 12 []

// With events and styling
calendar [
    HighlightStyle (Style.fg (Named Red) |> Style.withAttr Bold)
    TodayStyle (Style.fg (Named Cyan) |> Style.withAttr Underlined)
    WeekendStyle (Style.fg (Named BrightBlack))
    WeekStart Sunday
    Border BoxBorders.rounded
] 2024 12 [
    { Year = 2024; Month = 12; Day = 25; Style = Some (Style.fg (Named Green)) }
    { Year = 2024; Month = 12; Day = 31; Style = None }
]
```

## 7. .NET-Free Design Notes

- `DayOfWeek` is a custom DU — not `System.DayOfWeek`
- Date calculations are pure integer arithmetic — no `System.DateTime`
- `daysInMonth` and `dayOfWeek` use well-known algorithms (no BCL)
- Month/day names are string arrays — no `System.Globalization.CultureInfo`
- All rendering is pure: `(year, month, events) -> Region -> ViewTree`
- Localization is explicit (user passes day names) rather than implicit (CultureInfo)

### Localization Note

For i18n, users provide custom day/month names:
```fsharp
calendar [ DayNames ["Lu";"Ma";"Me";"Je";"Ve";"Sa";"Di"]
           MonthNames [...] ] year month events
```

This is simpler and more portable than depending on `CultureInfo`.

## 8. Acceptance Criteria

- [ ] Monthly grid renders correctly for any month/year
- [ ] Leap year handling correct
- [ ] Day-of-week calculation verified against known dates
- [ ] Week start configurable (Monday or Sunday)
- [ ] Event dates highlighted with configurable style
- [ ] Today highlighting
- [ ] Weekend styling
- [ ] Header with month name and year
- [ ] Day column headers (Mo Tu We...)
- [ ] IMeasurable with fixed minimum width
- [ ] Optional border around calendar
- [ ] No `System.DateTime`, `System.Globalization`, or `System.DayOfWeek`
- [ ] Date calculations are pure integer arithmetic
- [ ] Sample: calendar with event highlighting
