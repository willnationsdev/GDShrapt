using System.Threading;
using System.Threading.Tasks;
using GDShrapt.CLI.Core;
using GDShrapt.Semantics;

namespace GDShrapt.LSP;

/// <summary>
/// Handles textDocument/rangeFormatting.
///
/// GDShrapt's formatter is a whole-document safe formatter (only cosmetic changes: indentation,
/// spacing, blank lines). Partial (range-only) formatting is inherently context-dependent — a
/// fragment's correct indentation depends on its enclosing scope, and line endings are document-wide.
/// So this handler formats the whole document and returns the resulting edit; the requested range is
/// accepted but the formatting applies document-wide (a common, safe approximation).
/// </summary>
public class GDRangeFormattingHandler
{
    private readonly GDFormattingHandler _inner;

    public GDRangeFormattingHandler(IGDFormatHandler handler, GDProjectConfig? config = null)
    {
        _inner = new GDFormattingHandler(handler, config);
    }

    public Task<GDLspTextEdit[]?> HandleAsync(GDDocumentRangeFormattingParams @params, CancellationToken cancellationToken)
    {
        var docParams = new GDDocumentFormattingParams
        {
            TextDocument = @params.TextDocument,
            Options = @params.Options
        };
        return _inner.HandleAsync(docParams, cancellationToken);
    }
}
