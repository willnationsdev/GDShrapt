using System.Collections.Generic;
using System.IO;
using System.Linq;
using GDShrapt.Reader;
using GDShrapt.Semantics;

namespace GDShrapt.CLI.Core;

/// <summary>
/// Computes the cross-file edits needed when a project file is renamed/moved: every
/// preload/load("res://old") and path-extends "res://old" is rewritten to the new res:// path.
/// Only exact res:// references are updated (relative references are left as-is — a documented limit).
/// Used by LSP workspace/willRenameFiles.
/// </summary>
public interface IGDFileRenameHandler
{
    /// <summary>old/new are absolute file paths. Returns per-file edits (0-based ranges).</summary>
    IReadOnlyList<GDFileRenameEdit> GetRenameEdits(string oldPath, string newPath);
}

public class GDFileRenameEdit
{
    public string FilePath { get; init; } = "";
    public int StartLine { get; init; }
    public int StartColumn { get; init; }
    public int EndLine { get; init; }
    public int EndColumn { get; init; }
    public string NewText { get; init; } = "";
}

public class GDFileRenameHandler : IGDFileRenameHandler
{
    private readonly GDScriptProject _project;

    public GDFileRenameHandler(GDScriptProject project)
    {
        _project = project;
    }

    public IReadOnlyList<GDFileRenameEdit> GetRenameEdits(string oldPath, string newPath)
    {
        var root = _project.ProjectPath;
        if (string.IsNullOrEmpty(root))
            return [];

        var oldRes = ToResPath(root, oldPath);
        var newRes = ToResPath(root, newPath);
        if (oldRes == null || newRes == null || oldRes == newRes)
            return [];

        var newLiteral = "\"" + newRes + "\"";
        var edits = new List<GDFileRenameEdit>();

        foreach (var script in _project.ScriptFiles)
        {
            if (script?.Class == null || string.IsNullOrEmpty(script.FullPath))
                continue;

            foreach (var node in script.Class.AllNodes)
            {
                if (node is GDCallExpression call &&
                    call.CallerExpression is GDIdentifierExpression ident &&
                    IsResourceLoader(ident.Identifier?.Sequence) &&
                    call.Parameters?.FirstOrDefault() is GDStringExpression strExpr &&
                    strExpr.String?.Sequence == oldRes)
                {
                    AddEdit(edits, script.FullPath!, strExpr, newLiteral);
                }
            }

            if (script.Class.Extends?.Type is GDStringTypeNode pathExtends && pathExtends.Path?.Sequence == oldRes)
                AddEdit(edits, script.FullPath!, pathExtends, newLiteral);
        }

        return edits;
    }

    private static void AddEdit(List<GDFileRenameEdit> edits, string filePath, GDNode node, string newText)
    {
        edits.Add(new GDFileRenameEdit
        {
            FilePath = filePath.Replace('\\', '/'),
            StartLine = node.StartLine,
            StartColumn = node.StartColumn,
            EndLine = node.EndLine,
            EndColumn = node.EndColumn,
            NewText = newText
        });
    }

    private static bool IsResourceLoader(string? name) => name == "preload" || name == "load";

    private static string? ToResPath(string projectRoot, string absPath)
    {
        var root = Path.GetFullPath(projectRoot).Replace('\\', '/').TrimEnd('/');
        var abs = Path.GetFullPath(absPath).Replace('\\', '/');
        if (!abs.StartsWith(root + "/", System.StringComparison.OrdinalIgnoreCase))
            return null;
        return "res://" + abs.Substring(root.Length + 1);
    }
}
