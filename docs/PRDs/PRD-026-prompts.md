# PRD-026: Interactive Prompts

**Status:** Draft
**Tier:** 2 — Widget Catalog
**Dependencies:** PRD-001 (Segment), PRD-010 (Markup), PRD-012 (Live Rendering)
**Unlocks:** CLI wizards, configuration tools, interactive installers

---

## 1. Problem Statement

Interactive input beyond raw key bindings is essential for CLI applications — text entry with validation, selection from a list, multi-select with checkboxes, yes/no confirmation. Thuja's current input model is low-level key events mapped to messages. Users must build all input UI from scratch. Rich and Spectre.Console both provide high-level prompt widgets.

## 2. Reference Analysis

### Rich / Spectre.Console

**TextPrompt\<T\>:** Free-form text input with type conversion, validation, default values, secret mode (password masking), auto-complete suggestions.

**SelectionPrompt\<T\>:** Single-choice keyboard navigation with arrow keys, page scrolling, type-to-search, hierarchical groups.

**MultiSelectionPrompt\<T\>:** Checkbox-style multi-select with spacebar toggle, arrow navigation, required/optional selection.

**ConfirmationPrompt:** Simple yes/no with configurable default.

### Key Architectural Note

In Spectre.Console, prompts are *blocking* — they take over the terminal until answered. This is fundamentally at odds with the Elm architecture where all state changes flow through Update. For Thuja, prompts must be *non-blocking elements within the MVU loop*.

## 3. F# Native Design

### Prompt as Model+View+Update

Each prompt type is a mini Elm component — a model, view, and update triple that the user composes into their application:

```fsharp
/// Text input prompt state.
type TextInput = {
    Prompt: string
    Value: string
    CursorPos: int
    Mask: char option           // None = visible, Some '*' = masked
    Placeholder: string
    Validation: string -> Result<unit, string>
    Error: string option
}

type TextInputMsg =
    | InsertChar of char
    | DeleteBack
    | DeleteForward
    | MoveCursorLeft
    | MoveCursorRight
    | MoveCursorHome
    | MoveCursorEnd
    | Submit
    | Cancel

module TextInput =
    let init prompt = {
        Prompt = prompt; Value = ""; CursorPos = 0
        Mask = None; Placeholder = ""; Validation = fun _ -> Ok ()
        Error = None }

    let update (model: TextInput) : TextInputMsg -> TextInput * bool =
        // Returns (newModel, isSubmitted)
        function
        | InsertChar c ->
            let value = model.Value.Insert(model.CursorPos, string c)
            { model with Value = value; CursorPos = model.CursorPos + 1; Error = None }, false
        | DeleteBack when model.CursorPos > 0 ->
            let value = model.Value.Remove(model.CursorPos - 1, 1)
            { model with Value = value; CursorPos = model.CursorPos - 1; Error = None }, false
        | Submit ->
            match model.Validation model.Value with
            | Ok () -> model, true
            | Error msg -> { model with Error = Some msg }, false
        | Cancel -> model, true
        | _ -> model, false

    let view (model: TextInput) : Region -> ViewTree =
        fun region ->
            // [Prompt] [input value with cursor] [error message]
            ...

    let keyBindings : KeyInput * KeyModifiers -> TextInputMsg option =
        function
        | Char c, _ -> Some (InsertChar c)
        | Backspace, _ -> Some DeleteBack
        | Delete, _ -> Some DeleteForward
        | Left, _ -> Some MoveCursorLeft
        | Right, _ -> Some MoveCursorRight
        | Enter, _ -> Some Submit
        | Char 'c', KeyModifiers.Ctrl -> Some Cancel
        | _ -> None
```

### Selection Prompt

```fsharp
/// Single-selection prompt state.
type Selection<'a> = {
    Prompt: string
    Items: 'a list
    Display: 'a -> string       // How to display each item
    Selected: int               // Current index
    PageSize: int               // Visible items
    ScrollOffset: int           // First visible item index
    Filter: string option       // Type-to-search filter
}

type SelectionMsg =
    | MoveUp | MoveDown
    | PageUp | PageDown
    | FilterChar of char
    | ClearFilter
    | Confirm | Dismiss

module Selection =
    let init prompt items display = {
        Prompt = prompt; Items = items; Display = display
        Selected = 0; PageSize = 10; ScrollOffset = 0; Filter = None }

    let update (model: Selection<'a>) : SelectionMsg -> Selection<'a> * 'a option = ...
    let view (model: Selection<'a>) : Region -> ViewTree = ...
    // Renders:
    //   Prompt text
    //   > Item 1     (highlighted)
    //     Item 2
    //     Item 3
    //   (n more...)
```

### Multi-Selection Prompt

```fsharp
/// Multi-selection prompt state.
type MultiSelection<'a> = {
    Prompt: string
    Items: 'a list
    Display: 'a -> string
    Checked: Set<int>           // Indices of checked items
    Cursor: int
    PageSize: int
    ScrollOffset: int
    Required: bool              // Must select at least one
}

type MultiSelectionMsg =
    | MoveUp | MoveDown
    | ToggleCurrent             // Spacebar
    | SelectAll | DeselectAll
    | Confirm | Dismiss

module MultiSelection =
    // Renders:
    //   Prompt text
    //   [x] Item 1    (checked)
    //   [ ] Item 2    (unchecked, cursor here)
    //   [x] Item 3    (checked)
```

### Confirmation Prompt

```fsharp
/// Yes/No confirmation state.
type Confirmation = {
    Prompt: string
    Default: bool option    // Pre-selected answer
    Answer: bool option     // Current answer
}

module Confirmation =
    let init prompt = { Prompt = prompt; Default = None; Answer = None }
    // Renders: "Prompt text [y/N]" or "Prompt text [Y/n]"
```

## 4. Integration with Thuja Core

Prompts integrate as composable Elm components within the user's application:

```fsharp
type Model = {
    NameInput: TextInput
    Phase: Phase
}

type Msg =
    | NameMsg of TextInputMsg
    | ...

let update model = function
    | NameMsg msg ->
        let (input, submitted) = TextInput.update model.NameInput msg
        if submitted then
            { model with NameInput = input; Phase = NextPhase }, Cmd.none
        else
            { model with NameInput = input }, Cmd.none

let view model =
    match model.Phase with
    | EnterName -> TextInput.view model.NameInput
    | ...

let keyBindings = function
    | key when model.Phase = EnterName ->
        TextInput.keyBindings key |> Option.map (Cmd.ofMsg << NameMsg)
        |> Option.defaultValue Cmd.none
    | ...
```

This is fundamentally different from Rich/Spectre.Console's blocking prompts. It's more composable — prompts can appear alongside other elements, animate, and respond to external events. They're just more elements in the view tree.

## 5. Responsive Behavior

- Text input: input field expands to fill available width
- Selection: page size adapts to available height
- Multi-selection: same adaptive page size
- Filter text wraps or truncates as needed
- Error messages wrap below the input on narrow terminals

## 6. API Surface

```fsharp
// Text input
let nameInput = TextInput.init "Enter your name:"
    |> TextInput.withPlaceholder "John Doe"
    |> TextInput.withValidation (fun s ->
        if s.Length > 0 then Ok () else Error "Name required")

// Selection
let colorSelect = Selection.init "Pick a color:" colors (fun c -> c.Name)
    |> Selection.withPageSize 5

// Multi-selection
let features = MultiSelection.init "Select features:" allFeatures (fun f -> f.Name)
    |> MultiSelection.withRequired true

// Confirmation
let confirm = Confirmation.init "Delete all files?"
    |> Confirmation.withDefault false
```

## 7. .NET-Free Design Notes

- All prompt types are plain F# records
- Update functions are pure: `Model -> Msg -> Model * ResultSignal`
- View functions are pure: `Model -> Region -> ViewTree`
- Key binding functions are pure: `KeyInput * KeyModifiers -> Msg option`
- No `System.Console.ReadLine()` or blocking IO
- No threading — prompts are synchronous state machines within the MVU loop
- Type conversion for TextPrompt (parsing strings to int, float, etc.) is provided as user-supplied functions `string -> Result<'T, string>` — no `System.Convert`

## 8. Acceptance Criteria

- [ ] `TextInput`: text entry with cursor movement, insert, delete
- [ ] `TextInput`: secret mode (character masking)
- [ ] `TextInput`: validation with error display
- [ ] `TextInput`: placeholder text
- [ ] `Selection`: arrow key navigation with highlighting
- [ ] `Selection`: page scrolling when items exceed visible area
- [ ] `Selection`: type-to-search filtering
- [ ] `MultiSelection`: spacebar toggle, cursor navigation
- [ ] `MultiSelection`: select all / deselect all
- [ ] `MultiSelection`: required mode (at least one selected)
- [ ] `Confirmation`: yes/no with default highlighting
- [ ] All prompts composable within Elm MVU (non-blocking)
- [ ] All prompts render via view functions (not blocking IO)
- [ ] Key bindings exposed as pure functions
- [ ] Sample app: multi-step wizard using all prompt types
- [ ] No `System.Console.ReadLine` or blocking IO imports
