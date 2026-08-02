using System.Collections.Generic;
using System.IO;
using System.Linq;
using GDShrapt.Reader;
using GDShrapt.Semantics;

namespace GDShrapt.CLI.Core;

/// <summary>
/// Finds clickable file links in a script: preload/load("res://…" or relative) and
/// path-based extends ("res://…"). Used by LSP textDocument/documentLink.
/// </summary>
public interface IGDDocumentLinkHandler
{
    IReadOnlyList<GDDocumentLinkInfo> GetDocumentLinks(string filePath);
}

/// <summary>A document link: 0-based AST range + absolute target file path (forward slashes).</summary>
public class GDDocumentLinkInfo
{
    public int StartLine { get; init; }
    public int StartColumn { get; init; }
    public int EndLine { get; init; }
    public int EndColumn { get; init; }
    public string Target { get; init; } = "";
}

public class GDDocumentLinkHandler : IGDDocumentLinkHandler
{
    private readonly GDScriptProject _project;

    public GDDocumentLinkHandler(GDScriptProject project)
    {
        _project = project;
    }

    public IReadOnlyList<GDDocumentLinkInfo> GetDocumentLinks(string filePath)
    {
        var script = _project.GetScript(filePath);
        if (script?.Class == null)
            return [];

        var fileDir = Path.GetDirectoryName(filePath)?.Replace('\\', '/');
        var links = new List<GDDocumentLinkInfo>();

        // preload(...) / load(...) string arguments
        foreach (var node in script.Class.AllNodes)
        {
            if (node is GDCallExpression call &&
                call.CallerExpression is GDIdentifierExpression ident &&
                IsResourceLoader(ident.Identifier?.Sequence))
            {
                if (call.Parameters?.FirstOrDefault() is GDStringExpression strExpr && strExpr.String != null)
                    AddLink(links, strExpr, strExpr.String.Sequence, fileDir);
            }
        }

        // extends "res://path.gd"
        if (script.Class.Extends?.Type is GDStringTypeNode pathExtends && pathExtends.Path != null)
            AddLink(links, pathExtends.Path, pathExtends.Path.Sequence, fileDir);

        return links;
    }

    private void AddLink(List<GDDocumentLinkInfo> links, GDNode positionNode, string? path, string? fileDir)
    {
        var target = Resolve(path, fileDir);
        if (target == null)
            return;

        links.Add(new GDDocumentLinkInfo
        {
            StartLine = positionNode.StartLine,
            StartColumn = positionNode.StartColumn,
            EndLine = positionNode.EndLine,
            EndColumn = positionNode.EndColumn,
            Target = target
        });
    }

    // GDScript built-in resource loaders (GDWellKnownFunctions is internal to Semantics).
    private static bool IsResourceLoader(string? name) => name == "preload" || name == "load";

    private string? Resolve(string? path, string? fileDir)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        string abs;
        if (path.StartsWith("res://"))
        {
            var root = _project.ProjectPath;
            if (string.IsNullOrEmpty(root))
                return null;
            abs = Path.Combine(root, path.Substring(6));
        }
        else if (path.Contains("://"))
        {
            // user:// or other unresolvable schemes
            return null;
        }
        else
        {
            if (string.IsNullOrEmpty(fileDir))
                return null;
            abs = Path.Combine(fileDir, path);
        }

        abs = Path.GetFullPath(abs).Replace('\\', '/');
        return File.Exists(abs) ? abs : null;
    }
}
