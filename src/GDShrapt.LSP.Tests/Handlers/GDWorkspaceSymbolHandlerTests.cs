using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GDShrapt.Abstractions;
using GDShrapt.Semantics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FluentAssertions;

namespace GDShrapt.LSP.Tests;

[TestClass]
public class GDWorkspaceSymbolHandlerTests
{
    private static string TestProjectPath()
    {
        var baseDir = System.IO.Directory.GetCurrentDirectory();
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        return System.IO.Path.Combine(root, "testproject", "GDShrapt.TestProject");
    }

    private static GDWorkspaceSymbolHandler Setup()
    {
        var context = new GDDefaultProjectContext(TestProjectPath());
        var project = new GDScriptProject(context, new GDScriptProjectOptions());
        project.LoadScripts();
        project.AnalyzeAll();
        return new GDWorkspaceSymbolHandler(project);
    }

    [TestMethod]
    public async Task HandleAsync_QueryMatchesMethod_FindsIt()
    {
        var handler = Setup();
        var result = await handler.HandleAsync(new GDWorkspaceSymbolParams { Query = "take_damage" }, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Should().Contain(s => s.Name == "take_damage" && s.Kind == GDLspSymbolKind.Method);
    }

    [TestMethod]
    public async Task HandleAsync_EmptyQuery_ReturnsManySymbols()
    {
        var handler = Setup();
        var result = await handler.HandleAsync(new GDWorkspaceSymbolParams { Query = "" }, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Length.Should().BeGreaterThan(5, "an empty query returns all project symbols");
    }
}
