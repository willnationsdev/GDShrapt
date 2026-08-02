using System.Threading;
using System.Threading.Tasks;
using GDShrapt.CLI.Core;

namespace GDShrapt.LSP;

/// <summary>
/// Handles textDocument/prepareRename requests.
/// Thin wrapper over IGDRenameHandler from CLI.Core: returns the range of the identifier
/// token under the cursor (not the raw cursor position), so the rename box highlights the
/// whole symbol regardless of where inside it the cursor sits.
/// </summary>
public class GDLspPrepareRenameHandler
{
    private readonly IGDRenameHandler _renameHandler;

    public GDLspPrepareRenameHandler(IGDRenameHandler renameHandler)
    {
        _renameHandler = renameHandler;
    }

    public Task<GDPrepareRenameResult?> HandleAsync(GDPrepareRenameParams @params, CancellationToken cancellationToken)
    {
        var filePath = GDDocumentManager.UriToPath(@params.TextDocument.Uri);

        // Convert LSP 0-based to CLI.Core 1-based
        var line = @params.Position.Line + 1;
        var column = @params.Position.Character + 1;

        var range = _renameHandler.GetRenameRange(filePath, line, column);
        if (range == null || string.IsNullOrEmpty(range.Placeholder))
            return Task.FromResult<GDPrepareRenameResult?>(null);

        var col0 = range.Column - 1;
        var lspRange = GDLocationAdapter.ToLspRange(
            range.Line,
            col0,
            range.Line,
            col0 + range.Placeholder.Length);

        return Task.FromResult<GDPrepareRenameResult?>(new GDPrepareRenameResult
        {
            Range = lspRange,
            Placeholder = range.Placeholder
        });
    }
}
