using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GDShrapt.CLI.Core;

namespace GDShrapt.LSP;

/// <summary>
/// Handles workspace/willRenameFiles. Thin wrapper over IGDFileRenameHandler: builds a WorkspaceEdit
/// that rewrites res:// references to the renamed file(s). AST positions are 0-based — no conversion.
/// </summary>
public class GDLspFileRenameHandler
{
    private readonly IGDFileRenameHandler _handler;

    public GDLspFileRenameHandler(IGDFileRenameHandler handler)
    {
        _handler = handler;
    }

    public Task<GDWorkspaceEdit?> HandleAsync(GDRenameFilesParams @params, CancellationToken cancellationToken)
    {
        var changesByUri = new Dictionary<string, List<GDLspTextEdit>>();

        foreach (var file in @params.Files)
        {
            var oldPath = GDDocumentManager.UriToPath(file.OldUri);
            var newPath = GDDocumentManager.UriToPath(file.NewUri);

            foreach (var e in _handler.GetRenameEdits(oldPath, newPath))
            {
                var uri = GDDocumentManager.PathToUri(e.FilePath);
                if (!changesByUri.TryGetValue(uri, out var list))
                {
                    list = new List<GDLspTextEdit>();
                    changesByUri[uri] = list;
                }

                list.Add(new GDLspTextEdit
                {
                    Range = new GDLspRange(e.StartLine, e.StartColumn, e.EndLine, e.EndColumn),
                    NewText = e.NewText
                });
            }
        }

        if (changesByUri.Count == 0)
            return Task.FromResult<GDWorkspaceEdit?>(null);

        var changes = changesByUri.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
        return Task.FromResult<GDWorkspaceEdit?>(new GDWorkspaceEdit { Changes = changes });
    }
}
