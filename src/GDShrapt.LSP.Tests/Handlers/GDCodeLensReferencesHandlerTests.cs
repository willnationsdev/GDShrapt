using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GDShrapt.Abstractions;
using GDShrapt.CLI.Core;
using GDShrapt.Semantics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FluentAssertions;

namespace GDShrapt.LSP.Tests;

[TestClass]
public class GDCodeLensReferencesHandlerTests
{
    private static string TestProjectPath()
    {
        var baseDir = System.IO.Directory.GetCurrentDirectory();
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        return System.IO.Path.Combine(root, "testproject", "GDShrapt.TestProject");
    }

    private static (GDCodeLensReferencesHandler handler, IGDFindRefsHandler refs) Setup()
    {
        var context = new GDDefaultProjectContext(TestProjectPath());
        var project = new GDScriptProject(context, new GDScriptProjectOptions());
        project.LoadScripts();
        project.AnalyzeAll();
        var registry = new GDServiceRegistry();
        registry.LoadModules(project, new GDBaseModule());

        var codeLens = registry.GetService<IGDCodeLensHandler>()!;
        var refs = registry.GetService<IGDFindRefsHandler>()!;
        return (new GDCodeLensReferencesHandler(codeLens, refs), refs);
    }

    private static IEnumerable<GDCliReferenceLocation> FlattenCli(IEnumerable<GDReferenceGroup> groups)
    {
        foreach (var g in groups)
        {
            foreach (var l in g.Locations) yield return l;
            foreach (var l in FlattenCli(g.Overrides)) yield return l;
        }
    }

    private static string Script(string name) => System.IO.Path.Combine(TestProjectPath(), "test_scripts", name);

    [TestMethod]
    public async Task HandleAsync_ReturnsSameSetAsFindReferences_IncludingPotential()
    {
        var (handler, refs) = Setup();
        var filePath = Script("base_entity.gd");

        var cliCount = FlattenCli(refs.FindReferences("take_damage", filePath)).Count();

        var result = await handler.HandleAsync(new GDCodeLensReferencesParams
        {
            Uri = GDDocumentManager.PathToUri(filePath),
            SymbolName = "take_damage"
        }, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Length.Should().Be(cliCount, "code-lens references must match the find-refs set (reads = all)");
    }

    [TestMethod]
    public async Task HandleAsync_EmptySymbol_ReturnsNull()
    {
        var (handler, _) = Setup();

        var result = await handler.HandleAsync(new GDCodeLensReferencesParams
        {
            Uri = GDDocumentManager.PathToUri(Script("base_entity.gd")),
            SymbolName = ""
        }, CancellationToken.None);

        result.Should().BeNull();
    }
}
