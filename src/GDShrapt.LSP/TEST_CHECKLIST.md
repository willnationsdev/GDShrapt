# LSP Test Coverage Checklist

Living checklist for the GDShrapt language server. Each capability lists its handler, the
CLI.Core delegate, the **automated** coverage status, and **manual** VS Code steps.

Legend: ✅ covered · 🟡 thin/partial · ❌ no dedicated automated test

**Test-run policy:** run targeted `--filter` groups during development; full LSP.Tests /
SmokeTests only on explicit request. Build the server first, then point
`gdshrapt.server.path` at it and open `testproject/GDShrapt.TestProject`.

---

## Language features

| LSP method | Handler | CLI.Core delegate | Auto | Manual check |
|------------|---------|-------------------|------|--------------|
| textDocument/definition | GDDefinitionHandler | IGDGoToDefHandler | ✅ GDDefinitionHandlerTests + parity | F12 on local, cross-file, autoload, built-in type; lands on correct line/column |
| textDocument/references | GDReferencesHandler | IGDFindRefsHandler (+GoToDef) | ✅ GDReferencesHandlerTests + **parity** | Shift+F12; **count matches CLI `find-refs`** (incl. duck-typed); IncludeDeclaration toggle |
| textDocument/rename | GDLspRenameHandler | IGDRenameHandler (+GoToDef) | ✅ GDRenameHandlerTests + **parity** | F2 cross-file method; **only Strict edits applied** (no duck-typed collateral); overrides updated |
| textDocument/prepareRename | GDLspPrepareRenameHandler | IGDRenameHandler.GetRenameRange | ✅ **parity (range)** | F2 with cursor mid-identifier → box highlights the **whole** symbol |
| textDocument/hover | GDLspHoverHandler | IGDHoverHandler | ✅ GDHoverHandlerTests | Hover keyword pre-analysis; untyped var pre vs post analysis; literal → none |
| textDocument/completion | GDLspCompletionHandler | IGDCompletionHandler | ✅ GDCompletionHandlerTests (keywords/types/locals/overrides), GDNodePathCompletionTests | Triggers `.` `:` `(` `$` `/`; member list; `(` defers to signature help; in comment → none |
| textDocument/documentSymbol | GDDocumentSymbolHandler | IGDSymbolsHandler | ✅ GDDocumentSymbolHandlerTests | Ctrl+Shift+O lists methods/vars/signals + nested inner classes |
| textDocument/documentHighlight | GDLspDocumentHighlightHandler | IGDHighlightHandler (+GoToDef) | ✅ GDDocumentHighlightHandlerTests | Click symbol → all read/write occurrences highlighted |
| textDocument/formatting | GDFormattingHandler | IGDFormatHandler | ✅ GDFormattingHandlerTests | Format document; mixed indentation normalized; diagnostics line-map intact |
| textDocument/codeAction | GDLspCodeActionHandler | IGDCodeActionHandler | 🟡 GDCodeActionHandlerTests (contract); fix logic in GDFixProviderTests | Quick-fix on a diagnostic; refactor/source actions appear |
| textDocument/signatureHelp | GDLspSignatureHelpHandler | IGDSignatureHelpHandler | ✅ GDSignatureHelpHandlerTests (signature + params inside a call) | Type `print(`; signature shows; `,` advances active parameter |
| textDocument/inlayHint | GDLspInlayHintHandler | IGDInlayHintHandler | ✅ GDInlayHintHandlerTests | Parameter/return-type hints render at correct columns |
| textDocument/codeLens | GDLspCodeLensHandler | IGDCodeLensHandler | ✅ GDCodeLensHandlerTests | Reference counts appear after analysis; click → references; refresh after rename |
| gdshrapt/codeLensReferences | GDCodeLensReferencesHandler | IGDCodeLensHandler + IGDFindRefsHandler | ✅ GDCodeLensReferencesHandlerTests | Custom code-lens reference command returns same set as references |
| textDocument/foldingRange | GDLspFoldingRangeHandler | IGDFoldingRangeHandler | ✅ GDFoldingRangeHandlerTests | Fold class/func/if/for; multi-line comment folds |
| textDocument/semanticTokens/full | GDSemanticTokensHandler | IGDSemanticTokensHandler | ✅ GDSemanticTokensHandlerTests | `@abstract` italic; built-in virtual override accent; ranges correct |
| textDocument/typeDefinition | GDTypeDefinitionLspHandler | IGDTypeDefinitionHandler | ✅ GDTypeDefinitionHandlerTests | Go to type of a typed variable |
| textDocument/implementation | GDImplementationLspHandler | IGDImplementationHandler | ✅ GDImplementationHandlerTests | All overrides of a method across subclasses |
| callHierarchy/* | GDLspCallHierarchyHandler | IGDCallHierarchyHandler | ✅ GDCallHierarchyHandlerTests (prepare/incoming/outgoing, named callers/callees) | Incoming/outgoing on a recursive method (no hang); counts correct |
| workspace/symbol | GDWorkspaceSymbolHandler | (project AST scan) | ✅ GDWorkspaceSymbolHandlerTests | Search a `class_name`/member globally → file + location |
| workspace/executeCommand | (inline) | dispatch | ✅ GDExecuteCommandTests | `gdshrapt.serverStatus` |
| textDocument/selectionRange | GDLspSelectionRangeHandler | IGDSelectionRangeHandler | ✅ GDSelectionRangeHandlerTests | Expand-selection grows by AST containment; out-of-range graceful |
| textDocument/documentLink | GDLspDocumentLinkHandler | IGDDocumentLinkHandler | ✅ GDDocumentLinkHandlerTests | `preload`/`load`/path-`extends` res:// targets are clickable |
| textDocument/rangeFormatting | GDRangeFormattingHandler | IGDFormatHandler | ✅ GDRangeFormattingHandlerTests | Formats the whole document (safe formatter); edit only when changed |
| workspace/willRenameFiles | GDLspFileRenameHandler | IGDFileRenameHandler | ✅ GDFileRenameHandlerTests | Renaming a file rewrites res:// `preload`/`extends` refs project-wide |
| ~~gdshrapt/unionReferences~~ | — | — | ✅ removed | Dead request/handler/command retired; union refs now fold into find-references (Confidence=Union + SharedTypes) |

## Adapters / server infra

| Area | Auto | Notes |
|------|------|-------|
| Position conversion | ✅ GDLocationAdapterTests, GDNodeFinderTests | See `GDPositionContractTests` (CLI.Tests) for the column contract |
| Diagnostics adapter | ✅ GDDiagnosticAdapterTests | push model via GDDiagnosticPublisher |
| Document lifecycle | ✅ GDDocumentManagerTests | didOpen/didChange/didClose, URI↔path |
| Incremental sync | ✅ GDDocumentSyncTests | splice {range,text}; LF/CRLF, multibyte/UTF-16, out-of-range |
| Transports | ✅ GDJsonRpcTransportTests, GDSocketJsonRpcTransportTests | stdio + socket |
| Logger / trace / version / progress / showMessage | ✅ | Server/*Tests |

---

## Cross-cutting manual checks (not yet automated)

**Document sync & debounce** (**incremental** sync; syntax/semantic debounce configurable via
`initializationOptions`, defaults 300ms / 800ms) — splice logic unit-covered by `GDDocumentSyncTests`:
- [ ] Edit a file 10× in 2s → final state correct, no missed versions
- [ ] Hover during ongoing analysis → no crash, "Analysis in progress…" fallback
- [ ] Close file mid-edit → diagnostics cleared, no orphaned tasks
- [ ] Syntax errors appear ~300ms after last keystroke; semantic ~800ms

**Position edge cases:**
- [ ] Hover/def at EOF and EOL → no off-by-one, graceful null
- [ ] Column beyond line length / negative → handled gracefully
- [ ] Multi-byte / surrogate-pair line (emoji, CJK) → ranges land correctly

**Multi-file rename (Strict):**
- [ ] Rename method used across 3+ files → all updated, no duplicate edits
- [ ] Rename with overrides → base + all child overrides updated
- [ ] Duck-typed/potential references are **not** silently rewritten

**External changes:**
- [ ] Edit a `.tscn` outside the editor → attached scripts re-diagnosed (server-side watcher)
- [ ] `git checkout` changing files → analysis refreshes for open docs

**Scale / leaks:**
- [ ] Open a 5000-line script → hover < 2s, format < 5s
- [ ] Open/close a file 50× → memory stable

---

## Known gaps / follow-ups

- **codeAction** has a contract test (never throws / well-formed); the quick-fix *content* is
  covered by `GDFixProviderTests` (Semantics). A behavioral LSP test awaits a dedicated
  fixable-diagnostic fixture.
- **Responsiveness:** configurable debounce (`gdshrapt.diagnostics.syntaxDebounceMs` /
  `semanticDebounceMs`) and a per-request operation timeout (`gdshrapt.operationTimeoutMs`,
  applied to completion/codeLens/semanticTokens; 0 = off) are implemented. Debounce **timing**
  remains a manual check; per-request handler caching is still open.
- **Phase-5 features — DONE:** incremental document sync, selection range, document links, range
  formatting, and workspace `willRenameFiles` (res:// reference rewrite) are implemented and
  unit-tested. Remaining file-operation polish (relative-path reference rewrite, `didRenameFiles`)
  and other VS Code niceties (selection-range is server-side; document-link `resolve`) are optional.
