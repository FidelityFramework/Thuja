# PRD-020: Tree Widget

**Status:** Draft
**Tier:** 2 — Widget Catalog
**Dependencies:** PRD-000 (Measure), PRD-001 (Segment), PRD-010 (Markup), PRD-014 (Borders)
**Unlocks:** File system browsers, hierarchical data display, AST viewers

---

## 1. Problem Statement

Hierarchical data is common in terminal applications: file trees, dependency graphs, JSON/XML structures, organization charts, menu hierarchies. Thuja has no tree widget. Rich and Spectre.Console both provide tree rendering with guide lines, collapsible nodes, and styled labels.

## 2. Reference Analysis

### Rich (Python)

- `Tree(label)` with `tree.add(child)` for building
- Guide styles: normal (`│ ├── └──`), bold (`┃ ┣━━ ┗━━`), double (`║ ╠══ ╚══`), ascii
- Labels can be any renderable (Text, Panel, Table — not just strings)
- `__rich_measure__` walks entire tree, accounting for indent per level
- `__rich_console__` uses iterative stack-based traversal (not recursion)
- Inherited styles cascade from parent to children
- `guide_style` controls guide line appearance

### Spectre.Console (C#)

- `Tree(label)` with `tree.AddNode(child)`
- Same guide styles as Rich
- `TreeGuide` abstraction: `TreeGuide.Line`, `TreeGuide.DoubleLine`, `TreeGuide.BoldLine`, `TreeGuide.Ascii`

## 3. F# Native Design

### Data Model

```fsharp
/// A tree node containing a label and children.
type TreeNode<'a> = {
    Label: 'a
    Children: TreeNode<'a> list
}

module TreeNode =
    /// Create a leaf node.
    let leaf label = { Label = label; Children = [] }

    /// Create a node with children.
    let node label children = { Label = label; Children = children }

    /// Map over all labels in the tree.
    let rec map f tree =
        { Label = f tree.Label
          Children = tree.Children |> List.map (map f) }
```

### Guide Styles

```fsharp
/// Characters used for tree guide lines.
type TreeGuide = {
    Vertical: string     // │   continuing line
    Fork: string         // ├── intermediate child
    End: string          // └── last child
    Space: string        // "   " indent for non-continuing lines
}

module TreeGuide =
    let line = { Vertical = "│   "; Fork = "├── "; End = "└── "; Space = "    " }
    let bold = { Vertical = "┃   "; Fork = "┣━━ "; End = "┗━━ "; Space = "    " }
    let double = { Vertical = "║   "; Fork = "╠══ "; End = "╚══ "; Space = "    " }
    let ascii = { Vertical = "|   "; Fork = "+-- "; End = "`-- "; Space = "    " }
```

### Tree Element

```fsharp
/// Properties for tree rendering.
type TreeProps =
    | Guide of TreeGuide
    | GuideColor of Color
    | LabelStyle of Style
    | IndentWidth of int    // Override guide width (default: 4)

/// Render a tree where labels are strings (or markup strings).
let tree (props: TreeProps list) (root: TreeNode<string>) : Region -> ViewTree = ...

/// Render a tree where labels are arbitrary view functions.
let treeView (props: TreeProps list) (root: TreeNode<Region -> ViewTree>) : Region -> ViewTree = ...
```

### Rendering Algorithm

```fsharp
// Iterative stack-based traversal (following Rich's approach, avoiding stack overflow).
// For each node:
//   1. Build prefix from ancestor continuation flags
//   2. Render: prefix + guide char + label
//   3. Guide char = Fork if not last sibling, End if last
//   4. Prefix segments: Vertical if ancestor continues, Space if ancestor was last

// Each rendered line:
//   [guide prefix segments] [fork/end guide] [label text]
//   │   ├── Child 1
//   │   │   └── Grandchild
//   │   └── Child 2
//   └── Child 3
```

### Measurement

```fsharp
// IMeasurable: walk tree, measure each label, add indent
// Minimum = max(label_min + indent) across all nodes
// Maximum = max(label_max + indent) across all nodes
// indent = depth * guide_width (typically 4)
```

## 4. Integration with Thuja Core

The tree element integrates as a standard element in the `Elements` module. It produces Commands via segment composition (PRD-001): guide prefix segments + label segments per line, shaped to the Region.

Labels support markup (PRD-010), enabling styled tree nodes:
```fsharp
tree [] (TreeNode.node "[bold]src[/]" [
    TreeNode.node "[cyan]Components[/]" [
        TreeNode.leaf "[green]Button.fs[/]"
        TreeNode.leaf "[green]Input.fs[/]"
    ]
    TreeNode.leaf "[dim]README.md[/]"
])
```

## 5. Responsive Behavior

- Label truncation: when width is too narrow, labels truncate with ellipsis
- Deep trees: when indent exceeds available width, deeper nodes clip
- Measurement minimum = widest (guide_width + shortest_label) — ensures at least the label is partially visible

## 6. API Surface

```fsharp
// Simple string tree
tree [] (TreeNode.node "Root" [
    TreeNode.node "Branch 1" [ TreeNode.leaf "Leaf A"; TreeNode.leaf "Leaf B" ]
    TreeNode.node "Branch 2" [ TreeNode.leaf "Leaf C" ]
])

// Styled guides
tree [ Guide TreeGuide.bold; GuideColor (Named Cyan) ] rootNode

// From data (e.g., directory listing)
let dirTree = buildTreeFromPaths paths
tree [ Guide TreeGuide.line ] dirTree
```

## 7. .NET-Free Design Notes

- `TreeNode<'a>` is a generic F# record — no BCL collections
- `TreeGuide` is a record of string constants
- Tree traversal uses an explicit stack (F# list) — no System.Collections.Stack
- All rendering is pure: `TreeNode -> Region -> Command list`
- No `System.IO` for directory trees — that's user-space code building `TreeNode` values

## 8. Acceptance Criteria

- [ ] `TreeNode<'a>` type with generic labels and recursive children
- [ ] 4 guide styles: line, bold, double, ascii
- [ ] Iterative (non-recursive) rendering to handle deep trees
- [ ] Guide lines correctly show continuation vs termination
- [ ] Labels support markup text (PRD-010)
- [ ] `IMeasurable` implementation accounting for indent depth
- [ ] Label truncation on narrow terminals
- [ ] Guide color customization
- [ ] Sample app: file tree display
- [ ] No BCL dependencies in tree rendering
