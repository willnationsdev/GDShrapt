# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- **Local `const` declarations** — `const NAME = v`, `const NAME := v` and `const NAME: T = v` inside a
  function body now parse as a single `GDVariableDeclarationStatement` with `IsConstant == true`
  instead of separate expression statements (#21). Local constants are registered as constants, so
  GD5010 (ConstantReassignment) and the SCREAMING_SNAKE_CASE naming rule (GDL004) now apply to them,
  and GDL003 (snake_case) no longer reports them.
- **Builder** — `GD.Statement.Constant(...)` / `GD.Statement.AutoConstant(...)` factories for local constants.

### Fixed

- **Qualified type annotations in `for` loops** — `for t: MyClass.MyEnum in items:` now produces a
  `GDSubTypeNode` instead of leaking `.MyEnum` into the collection expression (#19). `GDForStatement` now
  delegates to `GDTypeResolver` like every other type-annotation site; the resolver completes a type when a
  second bare identifier follows, which is what keeps `for i: int in arr` working. Side effect: a type
  annotation followed by a line break no longer drops the type text from `ToString()`.
- **Multi-line ternary expressions** — `x = (\n a\n if c\n else b\n)` now keeps `FalseExpression` instead of
  dropping the `else` branch (#26). Works in parenthesized initializers, call arguments, array initializers
  and backslash continuations; exact round-trip is preserved.

### Changed

- **Position caching** — `Freeze()` now fills `StartLine` and `NewLinesCount` caches on every `GDNode` in a
  single pass, turning a whole-file position sweep from O(n²) into O(n). Unfrozen trees keep returning live
  values, and the paths that can mutate a frozen tree (`GDStringPart.Sequence`,
  `GDMultiLineSplitToken.Sequence`, and the `ProtectedSet` null→value carve-out) drop the caches.
  `EndLine` is now derived as `StartLine + NewLinesCount`, so the two can no longer disagree.
- `GDVariableDeclarationStatement` token slots shifted by one: `ConstKeyword` is slot 0, `VarKeyword` is
  slot 1. Named properties are unaffected; positional `Form` access must be renumbered.

## [6.0.0-alpha.7] - 2026-06-25

Pre-release of the 6.0 toolchain (the 5.x line shipped the parser, linter, and
formatter libraries; 6.0 adds the semantic analysis and tooling layer on top).
All Base project versions are now centralized in `Directory.Build.props` as a
single source of truth.

### Added

- **GDShrapt.Semantics** — project-level semantic analysis: SSA-style flow analysis
  with union types as the primary type source, duck-type inference, and refactoring
  services (rename, extract, add-type-annotations, reorder-members, and more).
- **GDShrapt.Semantics.Validator** — type-aware diagnostics (GD3xxx–GD9xxx) built on
  the semantic model, verified via TDD diagnostic markers.
- **GDShrapt.CLI** — command-line tooling: `analyze`, `check`, `lint`, `validate`,
  `format`, `symbols`, `find-refs`, `rename`, `parse`, `extract-style`.
- **GDShrapt.LSP** — Language Server Protocol 3.17 server sharing the CLI.Core handlers.
- **GDShrapt.Plugin** — Godot editor integration routed through the shared CLI.Core
  handler registry (completion, find-references, go-to-definition, rename, format,
  diagnostics, type-flow).

### Changed

- Library versions unified across all Base projects to a single centralized version.

## [5.1.0] - 2026-01-08

### Breaking Changes

- **License Change**: License changed from MIT to Apache License 2.0
  - Versions 5.0.0 and earlier remain under MIT License
  - Starting from 5.1.0, all code is under Apache License 2.0
  - Added NOTICE file as required by Apache 2.0

### New Packages

- **GDShrapt.Abstractions** - Base interfaces for file system, project context, and logging abstractions
  - `IGDFileSystem` - File system abstraction for testing and flexible integration
  - `IGDProjectContext` - Godot project context with path resolution
  - `IGDLogger` - Logging abstraction for GDShrapt tooling

- **GDShrapt.Semantics** - Godot-independent semantic analysis library
  - `GDScriptProject` - Project-level GDScript analysis
  - `GDTypeResolver` - Type inference and resolution
  - `GDSceneTypesProvider` - Node types from .tscn files
  - `GDProjectTypesProvider` - User-defined types from project scripts
  - `GDGodotTypesProvider` - Built-in Godot types from TypesMap
  - `GDRenameService` - Safe symbol renaming with conflict detection

- **GDShrapt.CLI** - Command-line tool for GDScript analysis
  - `analyze` - Analyze project and output diagnostics
  - `check` - CI/CD friendly error checking with exit codes
  - `symbols` - List symbols in a file
  - `find-refs` - Find references to a symbol
  - `rename` - Rename symbol across project
  - `format` - Format GDScript files

- **GDShrapt.LSP** - Language Server Protocol implementation
  - Code completion with type inference
  - Go to definition
  - Find all references
  - Hover information
  - Document symbols
  - Rename refactoring
  - Real-time diagnostics

- **GDShrapt.Plugin** - Godot Editor plugin (not published to NuGet)
  - Integration with Godot Editor
  - Auto-completion, go-to-definition, find references
  - Refactoring actions (extract method/variable, surround with)
  - Real-time diagnostics
  - TODO tags scanning

### Improvements

- Validator improvements for better error messages
- Type parsing enhancements in inference engine
- Added TypesMap submodule for Godot type information

### Internal

- Monorepo structure with all tools in single solution
- Removed commented/dead code
- Code cleanup and unused import removal

## [5.0.0] - 2026-01-02

### Breaking Changes

- Split into multiple NuGet packages for modularity:
  - `GDShrapt.Reader` - Core parsing and AST
  - `GDShrapt.Builder` - Fluent code generation API
  - `GDShrapt.Validator` - Compiler-style AST validation
  - `GDShrapt.Linter` - Style checking
  - `GDShrapt.Formatter` - Code formatting

### New Package: GDShrapt.Validator

- AST validation with compiler-style diagnostics (GD1xxx-GD7xxx)
- Syntax validation (GD1xxx): Invalid tokens, missing brackets, unexpected tokens
- Scope validation (GD2xxx): Undefined variables, duplicate declarations, forward references
- Type validation (GD3xxx): Type mismatches, invalid operand types
- Call validation (GD4xxx): Wrong argument counts for built-in functions
- Control flow validation (GD5xxx): break/continue outside loops, return outside functions
- Indentation validation (GD6xxx): Mixed tabs/spaces, inconsistent indentation
- Await validation (GD7xxx): Await expression structure issues
- Type inference system with `GDTypeInferenceEngine`
- `IGDRuntimeProvider` interface for custom type information (integrate with Godot)
- `GDDefaultRuntimeProvider` with built-in GDScript types and global functions
- `GDCachingRuntimeProvider` wrapper for performance optimization
- Two-pass validation with full forward reference support

### New Package: GDShrapt.Linter

- Style guide enforcement with configurable naming conventions
- Naming rules: ClassNameCaseRule, FunctionNameCaseRule, VariableNameCaseRule, ConstantNameCaseRule
- Signal/enum naming: SignalNameCaseRule, EnumNameCaseRule, EnumValueCaseRule
- Private member prefix checking (PrivatePrefixRule)
- Best practices: UnusedVariableRule, UnusedParameterRule, UnusedSignalRule, EmptyFunctionRule
- Code quality: TypeHintRule, MaxParametersRule, MaxFunctionLengthRule
- Style rules: LineLengthRule, MemberOrderingRule
- Strict typing rule (GDL215): Per-element configurable severity for required type hints
- Comment-based rule suppression (gdtoolkit compatible): `gdlint:ignore`, `gdlint:disable/enable`
- Support for both rule IDs (GDL001) and names (variable-name) in suppressions
- Presets: Default, Strict, Minimal

### New Package: GDShrapt.Formatter

- Rule-based code formatting (GDF001-GDF008)
- Indentation (GDF001): Tabs or spaces with configurable size
- Blank lines (GDF002): Between functions, after class declaration, between member types
- Spacing (GDF003): Around operators, after commas/colons, inside brackets
- Trailing whitespace (GDF004): Remove trailing spaces, ensure EOF newline
- Line endings (GDF005): Normalize to LF, CRLF, or Platform
- Line wrapping (GDF006): Automatic wrapping for long lines
- Auto type hints (GDF007): Automatically add inferred type hints using type inference
- Code reorder (GDF008): Reorder class members by type (opt-in)
- Style extraction from sample code ("format by example")
- LSP compatible options (tabSize, insertSpaces, trimTrailingWhitespace, etc.)
- Presets: Default, GDScriptStyleGuide, Minimal

### Parser Improvements

- Full GDScript 4.x support including typed dictionaries (Godot 4.4)
- Fixed property get/set parsing (Issues #10, #11)
- Custom `GDStackOverflowException` for controlled stack depth limits
- Configurable parsing limits via `GDReadSettings`

### New Features

- Helper classes: `GDAnnotationHelper`, `GDSpecialMethodHelper`, `GDExpressionHelper`
- Extended Building API: `GetUniqueNode`, `Enum`, `EnumValue`, Export annotations
- Comprehensive test coverage (1052 tests across all packages)

## [4.4.0-alpha] - 2025-05-19

- Added typed Dictionaries support (thanks to dougVanny)
- Added `Signals` property to `GDClassDeclaration`
- Fixed `CreateEmptyInstance` in some classes
- Added `.editorconfig` file

## [4.3.2-alpha] - 2025-03-29

- Fixed line breaks inside brackets, enums, dictionaries, arrays
- Fixed comments parsing inside brackets
- Fixed dual expression parsing at the end of line

## [4.3.1-alpha] - 2024-10-26

- Fixed lost characters and string line break parsing
- Fixed stack overflow exceptions

## [4.3.0-alpha] - 2024-06-16

- Fixed `get_node` expression parsing

## [4.2.0-alpha] - 2024-01-31

- Improved properties parsing and indentation parsing with multiline split token
- Reworked old attributes to class members
- Fixed comma bugs
- Improved same line attributes parsing
- Added new methods for attributes enumeration

## [4.1.1-alpha] - 2024-01-15

- Removed converter from repository
- Added guard condition parsing

## [4.1.0-alpha] - 2024-01-12

- Fixed indentation bugs in lambdas and match cases
- Fixed unique node expression
- Fixed subtype resolving
- Fixed indented comments parsing
- Fixed id extraction and visitor virtual methods

## [4.0.1-alpha] - 2023-12-23

- Fixed ClassName invalid token handling
- Minor updates and bugfixes

## [4.0.0-alpha] - 2023-12-21

Global rework to support GDScript 2.0 (Godot 4.0+). Older GDScript versions mostly are not supported now.

- Full GDScript 2.0 support: lambdas, await, typed arrays, new properties syntax, pattern matching
- Reworked attributes parsing for Godot 4.0 annotations
- Improved indentation parsing to match Godot behavior
- Fixed many StackOverflow exceptions during invalid code parsing
- Improved same-line expressions parsing
- Improved back slash parsing for line continuation

## [3.1.2-alpha] - 2022-11-26

- Small improvements of the declaration search in node's parents tree
- Invalid tokens now correctly handled for expressions
- Handled multiple expressions on the same line

## [3.1.1-alpha] - 2022-11-18

- Fixed parsing of the dual expression splitting
- Fixed some parsing errors
- Added global next and previous tokens search

## [3.1.0-alpha] - 2022-01-31

- Improved NodePath parsing
- Fixed issues with 'elif' branches and some expressions sort order
- Improved inner classes parsing
- Improved node walking and visiting
- Fixed small parsing errors

## [3.0.0-alpha] - 2021-07-13

- Many small fixes with the if-elif-else branches parsing
- Improved tree management
- Fixed 'newline' character parsing in multiline expressions and initializers
- Fixed Yield parsing
- Most internal methods are now public
- 'Form' of every node is now accessible from user code
- Implemented two styles of code generation
- Implemented many additional properties for tokens and nodes (StartLine, Length, etc.)
- Implemented method for parsing unspecified content

## [2.1.0-alpha] - 2021-06-26

- Implemented 'clone' methods for every token type
- Changed project type to .NET Standard 2.0 (supported by Godot)
- Fixed `ToString` method of `GDIndexerExpression`

## [2.0.0-alpha] - 2021

- Migrated to .NET Standard 2.1
- Totally reworked with tokenization layer
- Parser now performs token extraction and lexical tree construction at the same time
- No style data loss
- Possibility to manage every token in code
- Implemented specific node parsing like NodePath, short form of 'get_node'
- Properly handling of comments and spaces

## [1.0.0-prealpha] - 2021

- Initial .NET 5.0 version
- Implemented all basic nodes and lexical tree building
- Has limitations in specific situations and style data loss
