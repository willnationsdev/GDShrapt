using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GDShrapt.CLI.Core;

namespace GDShrapt.LSP;

/// <summary>
/// Handles textDocument/rename requests.
/// Thin wrapper over IGDRenameHandler from CLI.Core.
/// Uses IGDGoToDefHandler to get symbol name at cursor position.
/// </summary>
public class GDLspRenameHandler
{
    private readonly IGDRenameHandler _renameHandler;
    private readonly IGDGoToDefHandler _goToDefHandler;

    public GDLspRenameHandler(IGDRenameHandler renameHandler, IGDGoToDefHandler goToDefHandler)
    {
        _renameHandler = renameHandler;
        _goToDefHandler = goToDefHandler;
    }

    public Task<GDWorkspaceEdit?> HandleAsync(GDRenameParams @params, CancellationToken cancellationToken)
    {
        var filePath = GDDocumentManager.UriToPath(@params.TextDocument.Uri);

        var newName = @params.NewName;
        if (string.IsNullOrWhiteSpace(newName))
            return Task.FromResult<GDWorkspaceEdit?>(null);

        // Resolve the symbol at the cursor (shared with references; identical to the CLI path)
        var oldName = GDLspCursorSymbol.ResolveName(_goToDefHandler, filePath, @params.Position);
        if (oldName == null)
            return Task.FromResult<GDWorkspaceEdit?>(null);

        // Validate the new name
        if (!_renameHandler.ValidateIdentifier(newName, out _))
            return Task.FromResult<GDWorkspaceEdit?>(null);

        // Delegate to CLI.Core handler
        var result = _renameHandler.Plan(oldName, newName, filePath);

        // LSP applies Strict edits only — never duck-typed/potential edits (LSP = Strict mode).
        // Potential edits are surfaced by the CLI; the editor must not silently rewrite them.
        if (!result.Success || result.StrictEdits.Count == 0)
            return Task.FromResult<GDWorkspaceEdit?>(null);

        // Convert CLI.Core strict edits to LSP workspace edit
        var changes = ConvertToWorkspaceEdit(result.StrictEdits);

        return Task.FromResult<GDWorkspaceEdit?>(new GDWorkspaceEdit
        {
            Changes = changes
        });
    }

    private static Dictionary<string, GDLspTextEdit[]> ConvertToWorkspaceEdit(IReadOnlyList<GDTextEdit> edits)
    {
        var changesByFile = new Dictionary<string, List<GDLspTextEdit>>();

        foreach (var edit in edits)
        {
            var uri = GDDocumentManager.PathToUri(edit.FilePath);

            if (!changesByFile.TryGetValue(uri, out var fileEdits))
            {
                fileEdits = new List<GDLspTextEdit>();
                changesByFile[uri] = fileEdits;
            }

            // Convert 1-based line/column to 0-based LSP positions
            var startLine = edit.Line - 1;
            var startColumn = edit.Column - 1;
            var endLine = startLine;
            var endColumn = startColumn + edit.OldText.Length;

            fileEdits.Add(new GDLspTextEdit
            {
                Range = new GDLspRange(startLine, startColumn, endLine, endColumn),
                NewText = edit.NewText
            });
        }

        // Convert to arrays
        var result = new Dictionary<string, GDLspTextEdit[]>();
        foreach (var kvp in changesByFile)
        {
            result[kvp.Key] = kvp.Value.ToArray();
        }

        return result;
    }
}
