using System.Collections.Generic;
using GDShrapt.Reader;
using GDShrapt.Semantics;

namespace GDShrapt.CLI.Core;

/// <summary>
/// Computes the AST-containment chain of ranges around a position, used by
/// the LSP textDocument/selectionRange (expand/shrink selection).
/// </summary>
public interface IGDSelectionRangeHandler
{
    /// <summary>
    /// Returns the nested ranges from innermost (the token under the cursor) to outermost
    /// (the class). Input line/column are 1-based; output spans are 0-based (AST). Empty if none.
    /// </summary>
    IReadOnlyList<GDSelectionSpan> GetSelectionRanges(string filePath, int line, int column);
}

/// <summary>0-based AST span (start inclusive, end exclusive on column).</summary>
public class GDSelectionSpan
{
    public int StartLine { get; init; }
    public int StartColumn { get; init; }
    public int EndLine { get; init; }
    public int EndColumn { get; init; }
}

public class GDSelectionRangeHandler : IGDSelectionRangeHandler
{
    private readonly GDScriptProject _project;

    public GDSelectionRangeHandler(GDScriptProject project)
    {
        _project = project;
    }

    public IReadOnlyList<GDSelectionSpan> GetSelectionRanges(string filePath, int line, int column)
    {
        var script = _project.GetScript(filePath);
        if (script?.Class == null)
            return [];

        // 1-based input → 0-based AST
        if (!script.Class.TryGetTokenByPosition(line - 1, column - 1, out var token) || token == null)
            return [];

        var spans = new List<GDSelectionSpan>();
        AddSpan(spans, token.StartLine, token.StartColumn, token.EndLine, token.EndColumn);

        var node = token.Parent;
        while (node != null)
        {
            AddSpan(spans, node.StartLine, node.StartColumn, node.EndLine, node.EndColumn);
            node = node.Parent;
        }

        return spans;
    }

    private static void AddSpan(List<GDSelectionSpan> spans, int sl, int sc, int el, int ec)
    {
        // Skip degenerate ranges.
        if (el < sl || (el == sl && ec <= sc))
            return;

        // AST ancestors always contain descendants; skip a range identical to the previous one.
        if (spans.Count > 0)
        {
            var last = spans[spans.Count - 1];
            if (last.StartLine == sl && last.StartColumn == sc && last.EndLine == el && last.EndColumn == ec)
                return;
        }

        spans.Add(new GDSelectionSpan { StartLine = sl, StartColumn = sc, EndLine = el, EndColumn = ec });
    }
}
