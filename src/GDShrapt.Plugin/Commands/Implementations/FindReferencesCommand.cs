using GDShrapt.CLI.Core;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GDShrapt.Plugin;

internal class FindReferencesCommand : Command
{
    private ReferencesDock _referencesDock;

    public FindReferencesCommand(GDShraptPlugin plugin)
        : base(plugin)
    {
    }

    internal void SetReferencesDock(ReferencesDock dock)
    {
        _referencesDock = dock;
    }

    public override Task Execute(IScriptEditor controller)
    {
        Logger.Info("Find References requested");

        if (!controller.IsValid)
        {
            Logger.Info("Find References cancelled: Editor is not valid");
            return Task.CompletedTask;
        }

        var line = controller.CursorLine;     // 0-based
        var column = controller.CursorColumn; // 0-based

        var @class = controller.GetClass();
        if (@class == null)
        {
            Logger.Info("Find References cancelled: no class declaration");
            return Task.CompletedTask;
        }

        // Resolve the identifier under the cursor (editor-side, on the parsed class).
        var finder = new GDPositionFinder(@class);
        var identifier = finder.FindIdentifierAtPosition(line, column);
        if (identifier == null)
        {
            Logger.Info("Find References cancelled: no identifier at cursor");
            return Task.CompletedTask;
        }

        var symbolName = identifier.Sequence;
        Logger.Info($"Finding references for '{symbolName}'");

        // Delegate to the shared core handler (same path as CLI/LSP — includes cross-file).
        var findRefs = Plugin.ServiceRegistry.GetService<IGDFindRefsHandler>();
        if (findRefs == null)
        {
            Logger.Info("Find References cancelled: handler not available");
            return Task.CompletedTask;
        }

        var filePath = controller.ScriptFile?.FullPath ?? controller.ScriptPath;
        var groups = findRefs.FindReferences(symbolName, filePath);

        var allReferences = new List<ReferenceItem>();
        foreach (var group in groups)
            CollectGroup(group, symbolName, allReferences);

        Logger.Info($"Found {allReferences.Count} references for '{symbolName}'");

        _referencesDock?.ShowReferences(symbolName, allReferences);

        return Task.CompletedTask;
    }

    private static void CollectGroup(GDReferenceGroup group, string symbolName, List<ReferenceItem> output)
    {
        foreach (var loc in group.Locations)
            output.Add(ToReferenceItem(loc, symbolName));

        foreach (var ov in group.Overrides)
            CollectGroup(ov, symbolName, output);
    }

    /// <summary>
    /// Converts a core reference location (1-based line/column) to the dock's 0-based ReferenceItem.
    /// </summary>
    private static ReferenceItem ToReferenceItem(GDCliReferenceLocation loc, string symbolName)
    {
        var line0 = GDPluginPositionAdapter.FromLine(loc.Line);
        var col0 = GDPluginPositionAdapter.FromRefColumn(loc.Column);
        var endCol0 = col0 + symbolName.Length;

        var (context, hlStart, hlEnd) = BuildContext(loc.Context, symbolName);

        var kind = loc.IsDeclaration ? GDPluginReferenceKind.Declaration
                 : loc.IsWrite ? GDPluginReferenceKind.Write
                 : GDPluginReferenceKind.Read;

        return new ReferenceItem(loc.FilePath, line0, col0, endCol0, context, kind, hlStart, hlEnd);
    }

    /// <summary>
    /// Trims/truncates the source line and locates the symbol for highlighting (matches the dock's
    /// previous display behaviour).
    /// </summary>
    private static (string context, int highlightStart, int highlightEnd) BuildContext(string? sourceLine, string symbolName)
    {
        var text = sourceLine ?? "";
        var wasTruncated = false;

        if (text.Length > 60)
        {
            text = text.Substring(0, 57) + "...";
            wasTruncated = true;
        }

        text = text.Trim().Replace("\n", " ").Replace("\r", "");

        int highlightStart = 0, highlightEnd = 0;
        if (!string.IsNullOrEmpty(symbolName))
        {
            var idx = text.IndexOf(symbolName, System.StringComparison.Ordinal);
            if (idx >= 0 && (!wasTruncated || idx + symbolName.Length <= 57))
            {
                highlightStart = idx;
                highlightEnd = idx + symbolName.Length;
            }
        }

        return (text, highlightStart, highlightEnd);
    }
}
