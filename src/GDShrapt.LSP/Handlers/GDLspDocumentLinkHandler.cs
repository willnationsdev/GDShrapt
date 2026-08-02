using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GDShrapt.CLI.Core;

namespace GDShrapt.LSP;

/// <summary>
/// Handles textDocument/documentLink. Thin wrapper over IGDDocumentLinkHandler.
/// AST positions are 0-based, LSP documentLink is 0-based — no output conversion.
/// </summary>
public class GDLspDocumentLinkHandler
{
    private readonly IGDDocumentLinkHandler _handler;

    public GDLspDocumentLinkHandler(IGDDocumentLinkHandler handler)
    {
        _handler = handler;
    }

    public Task<GDDocumentLink[]?> HandleAsync(GDDocumentLinkParams @params, CancellationToken cancellationToken)
    {
        var filePath = GDDocumentManager.UriToPath(@params.TextDocument.Uri);
        var links = _handler.GetDocumentLinks(filePath);
        if (links.Count == 0)
            return Task.FromResult<GDDocumentLink[]?>(null);

        var result = links.Select(l => new GDDocumentLink
        {
            Range = new GDLspRange(l.StartLine, l.StartColumn, l.EndLine, l.EndColumn),
            Target = GDDocumentManager.PathToUri(l.Target)
        }).ToArray();

        return Task.FromResult<GDDocumentLink[]?>(result);
    }
}
