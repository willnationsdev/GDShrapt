using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GDShrapt.CLI.Core;

namespace GDShrapt.LSP;

/// <summary>
/// Handles textDocument/selectionRange. Thin wrapper over IGDSelectionRangeHandler.
/// AST spans are 0-based, LSP selectionRange is 0-based — no output conversion.
/// </summary>
public class GDLspSelectionRangeHandler
{
    private readonly IGDSelectionRangeHandler _handler;

    public GDLspSelectionRangeHandler(IGDSelectionRangeHandler handler)
    {
        _handler = handler;
    }

    public Task<GDSelectionRange[]?> HandleAsync(GDSelectionRangeParams @params, CancellationToken cancellationToken)
    {
        var filePath = GDDocumentManager.UriToPath(@params.TextDocument.Uri);
        var result = new List<GDSelectionRange>(@params.Positions.Count);

        foreach (var pos in @params.Positions)
        {
            // LSP 0-based → CLI.Core 1-based
            var spans = _handler.GetSelectionRanges(filePath, pos.Line + 1, pos.Character + 1);
            result.Add(BuildChain(spans, pos));
        }

        return Task.FromResult<GDSelectionRange[]?>(result.ToArray());
    }

    private static GDSelectionRange BuildChain(IReadOnlyList<GDSelectionSpan> spans, GDLspPosition position)
    {
        if (spans.Count == 0)
        {
            // LSP requires one entry per input position; collapse to a zero-width range at the cursor.
            return new GDSelectionRange
            {
                Range = new GDLspRange(position.Line, position.Character, position.Line, position.Character)
            };
        }

        // spans are innermost-first → build the linked list so each node's Parent is the next (outer) range.
        GDSelectionRange? parent = null;
        for (int i = spans.Count - 1; i >= 0; i--)
        {
            var s = spans[i];
            parent = new GDSelectionRange
            {
                Range = new GDLspRange(s.StartLine, s.StartColumn, s.EndLine, s.EndColumn),
                Parent = parent
            };
        }

        return parent!;
    }
}
