# GDShrapt.Plugin

Godot Editor integration plugin. Production-ready (~95% complete).

## Commands (7)

| Command | Shortcut | Status |
|---------|----------|--------|
| AutoComplete | Ctrl+Tab | Complete |
| ExtractMethod | Ctrl+E | Complete |
| FormatCode | Alt+F | Complete |
| GoToDefinition | F12 | Complete |
| RemoveComments | Ctrl+P | Complete |
| Rename | F2 / Ctrl+R | Complete |
| FindReferences | - | Complete |

## Docks (5 bottom panels)

| Dock | Purpose |
|------|---------|
| Problems | Diagnostics with grouping/filtering |
| References | Find refs results with context |
| TODO Tags | Scans TODO/FIXME/HACK/NOTE |
| AST Viewer | Parse tree visualization |
| REPL | Expression evaluation |

## UI Panels

| Panel | Purpose |
|-------|---------|
| TypeFlowPanel | Type inference graph visualization |
| NotificationPanel | Diagnostic summary (corner) |
| QuickFixesPopup | Inline fix suggestions |
| RenamingDialog | Symbol rename input |
| NodeRenamingDialog | Scene node rename sync |
| AboutPanel | Plugin info |
| TodoTagsSettingsPanel | Tag configuration |

## Completion

Completion **content** comes from the core `IGDCompletionHandler` (single source of truth, shared with
CLI/LSP). The Plugin only detects the editor context from the live buffer (`GDCompletionContextBuilder`),
maps it to a `GDCompletionRequest`, and renders the result in the popup (`GDCompletionItemConverter` →
`GDCompletionPopup`). Provides:

- **Symbol completions**: locals, methods, signals, constants, keywords
- **Member access** (after `.`): methods, properties from Godot/project types
- **Type annotations** (after `:`): built-in types, Godot classes, project types
- Built-in Godot functions, GDScript keywords, and snippets (all from the handler)

## Refactoring Actions (9)

| Action | ID | Base | Pro |
|--------|-----|------|-----|
| SurroundWithIf | `surround_with_if` | Execute | Execute |
| ExtractConstant | `extract_constant` | Execute | Execute |
| ExtractVariable | `extract_variable` | Execute | Execute |
| GenerateGetterSetter | `generate_getter_setter` | Execute | Execute |
| InvertCondition | `invert_condition` | Execute | Execute |
| AddTypeAnnotation | `add_type_annotation` | Preview | Execute |
| ConvertForToWhile | `convert_for_to_while` | Preview | Execute |
| MoveGetNodeToOnready | `move_getnode_to_onready` | Preview | Execute |
| ReorderMembers | - | Preview | Execute |

**Shortcuts:** Ctrl+Alt+C (extract const), Ctrl+Alt+V (extract var), Ctrl+Alt+G (getter/setter)

## Diagnostics

- Real-time validation + linting
- Background analysis with priority queue
- Severity levels: Error, Warning, Hint, Info
- Inline markers, gutter annotations, notification panel

### Coordinate System

| Component | Line | Column | Notes |
|-----------|------|--------|-------|
| AST (Parser) | 0-based | 0-based | Internal representation |
| GDDiagnostic (Validator) | **1-based** | **0-based** | Validator output |
| GDLintIssue (Linter) | **1-based** | **0-based** | Linter output |
| GDUnifiedDiagnostic (Semantics) | **1-based** | **0-based** | Unified diagnostic format |
| GDPluginDiagnostic | **0-based** | **0-based** | Plugin internal (after adapter) |
| Godot CodeEdit API | **0-based** | **0-based** | SetLineBackgroundColor, etc. |
| Godot EditScript API | **1-based** | **0-based** | EditorInterface.EditScript() |

### Diagnostic Flow

```
GDDiagnosticsService (Semantics)
    ↓ GDUnifiedDiagnostic (Line 1-based, Column 0-based)
GDPluginDiagnosticAdapter.Convert()
    ↓ Line -1 (convert to 0-based), Column unchanged
GDPluginDiagnostic (Line 0-based, Column 0-based)
    ↓
ProblemsDock.CreateDiagnosticRow()
    ↓ Display: Line +1 (show 1-based to user)
    ↓ Metadata: stores 0-based Line for navigation
UI shows: "Line N" (1-based, human-readable)
    ↓
ProblemsDock.OnItemActivated()
    ↓ NavigateToItem(line + 1) → passes 1-based line
GDShraptPlugin.OnNavigateToReference()
    ↓ EditScript(line, column) → line is already 1-based
Godot navigates to correct line
```

### Key Files

| File | Responsibility |
|------|----------------|
| `GDPluginDiagnosticAdapter.cs` | Converts Semantics → Plugin coordinates |
| `GDPluginDiagnostic.cs` | Plugin diagnostic model (0-based) |
| `ProblemsDock.cs` | UI display (+1 for user) and navigation |
| `GDShraptPlugin.cs` | EditScript navigation (expects 1-based) |

## Core integration

The Plugin is a thin consumer of the shared CLI.Core handler registry (the same path as the LSP):
it builds ONE `GDShrapt.Semantics.GDScriptProject` (analyzed once) and loads `GDServiceRegistry` +
`GDBaseModule` (`GDShraptPlugin.cs`), then resolves `IGD*Handler` via `Plugin.ServiceRegistry` and
renders the result. Routed through handlers: **completion** (`IGDCompletionHandler`), **find references**
(`IGDFindRefsHandler`), **go-to-definition** (`IGDGoToDefHandler`), **rename** (`IGDRenameHandler`,
cross-file), **format** (`IGDFormatHandler`), **diagnostics** (`GDDiagnosticsHandler`), **type flow**
(`IGDTypeFlowHandler`). Position conversion between Godot (0-based) and the handler contract lives in one
place — `Infrastructure/GDPluginPositionAdapter.cs`.

**Kept Plugin-local (Godot glue / no handler equivalent):** all UI (docks/panels/dialogs/controls),
`TabController`, gutters, the completion popup, scene watching; the node-path/scene rename
(`RenameIdentifierCommand` `RenamePathListNode*` via `GDRenameService.PlanNodePathRename` +
`NodeRenamingDialog`); and the interactive single-file refactoring actions (`Refactoring/Actions/*` over
`GDExtract*`/`GDGenerate*`/… services — they need action-specific dialogs/preview the code-action handler
can't express).

## Known Limitations

1. **Coordinate Systems** - Mixed 0-based/1-based across components (see table above)
2. **Background Analysis** - May lag on large projects (priority queue helps)
3. **Scene Sync** - Node renames only, not path refactoring
4. **TypeFlow** - Single method visualization only
5. **Rename preview** - symbol rename applies cross-file via the handler, but the rename dialog does not
   yet list the multi-file edit set before applying (name entry only)
6. **REPL (experimental)** - evaluates expressions against the live scene context: literals, identifiers,
   member/index/call, operators (incl. `is`/`in`), ternary, array/dictionary literals, node access
   (`$Path`/`%Unique`), and `&"name"`/`^"path"` literals. `await`/`preload`/assignment/`match` are not
   evaluated (a clear message is shown). Godot-runtime only — not covered by CI tests; keeps its
   experimental disclaimer by design.
7. **TypeFlow apply** - "add type guard" and "generate interface from duck types" are **preview-only** in
   Base (copy the result to apply manually). Executing them is a Pro capability (STATE.md Rule 20)

## Other Features

- **Scene file watching**: Auto-detects node renames, syncs to GDScript
- **Cache management**: Content-hash invalidation
- **Project Settings integration**: UI in Project → Settings → GDShrapt/
- **Localization**: Multi-language support

## Key Files

```
GDShraptPlugin.cs (main entry point)

Commands/
├── AutoCompleteCommand.cs
├── ExtractMethodCommand.cs
├── FormatCodeCommand.cs
├── GoToDefinitionCommand.cs
├── RemoveCommentsCommand.cs
├── RenameCommand.cs
└── FindReferencesCommand.cs

Completion/
├── GDCompletionContext.cs       (editor-side context detection)
├── GDCompletionItem.cs          (popup item model)
├── GDCompletionItemConverter.cs (CLI.Core item → popup item)
└── GDCompletionPopup.cs         (popup UI)

Diagnostics/
├── GDPluginDiagnosticService.cs
├── GDBackgroundAnalyzer.cs
└── GDDiagnosticPublisher.cs

Layout/
├── ProblemsDock.cs
├── ReferencesDock.cs
├── TodoTagsDock.cs
├── AstViewerDock.cs
└── ReplDock.cs

Refactoring/
├── GDRefactoringActionProvider.cs
└── Actions/*.cs

TypeFlow/
├── GDTypeFlowGraphBuilder.cs
└── TypeFlowPanel.cs

UI/
├── NotificationPanel.cs
├── QuickFixesPopup.cs
├── RenamingDialog.cs
└── NodeRenamingDialog.cs
```
