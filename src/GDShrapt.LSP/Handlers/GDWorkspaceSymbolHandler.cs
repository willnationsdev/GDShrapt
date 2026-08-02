using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GDShrapt.Reader;
using GDShrapt.Semantics;

namespace GDShrapt.LSP;

/// <summary>
/// Handles workspace/symbol requests: project-wide symbol search across all loaded scripts.
/// Extracted from the server so it is testable and keeps the server a pure dispatcher.
/// </summary>
public class GDWorkspaceSymbolHandler
{
    private readonly GDScriptProject _project;

    public GDWorkspaceSymbolHandler(GDScriptProject project)
    {
        _project = project;
    }

    public Task<GDLspSymbolInformation[]?> HandleAsync(GDWorkspaceSymbolParams @params, CancellationToken cancellationToken)
    {
        var query = @params.Query?.ToLowerInvariant() ?? "";
        var results = new List<GDLspSymbolInformation>();

        foreach (var script in _project.ScriptFiles)
        {
            if (script.Class == null || script.FullPath == null)
                continue;

            var uri = GDDocumentManager.PathToUri(script.FullPath);

            foreach (var member in script.Class.Members)
            {
                string? name = null;
                var kind = GDLspSymbolKind.Variable;
                var line = 0;

                if (member is GDMethodDeclaration method)
                {
                    name = method.Identifier?.ToString();
                    kind = GDLspSymbolKind.Method;
                    line = method.StartLine;
                }
                else if (member is GDVariableDeclaration variable)
                {
                    name = variable.Identifier?.ToString();
                    kind = variable.IsConstant ? GDLspSymbolKind.Constant : GDLspSymbolKind.Variable;
                    line = variable.StartLine;
                }
                else if (member is GDSignalDeclaration signal)
                {
                    name = signal.Identifier?.ToString();
                    kind = GDLspSymbolKind.Event;
                    line = signal.StartLine;
                }
                else if (member is GDEnumDeclaration enumDecl)
                {
                    name = enumDecl.Identifier?.ToString();
                    kind = GDLspSymbolKind.Enum;
                    line = enumDecl.StartLine;
                }
                else if (member is GDInnerClassDeclaration innerClass)
                {
                    name = innerClass.Identifier?.ToString();
                    kind = GDLspSymbolKind.Class;
                    line = innerClass.StartLine;
                }

                if (name != null && (string.IsNullOrEmpty(query) || name.ToLowerInvariant().Contains(query)))
                {
                    results.Add(new GDLspSymbolInformation
                    {
                        Name = name,
                        Kind = kind,
                        Location = new GDLspLocation
                        {
                            Uri = uri,
                            Range = new GDLspRange
                            {
                                Start = new GDLspPosition { Line = line, Character = 0 },
                                End = new GDLspPosition { Line = line, Character = 0 }
                            }
                        }
                    });
                }
            }
        }

        return Task.FromResult<GDLspSymbolInformation[]?>(results.ToArray());
    }
}
