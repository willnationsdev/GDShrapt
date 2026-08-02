using System.Threading;
using System.Threading.Tasks;
using GDShrapt.Abstractions;
using GDShrapt.CLI.Core;
using GDShrapt.Semantics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FluentAssertions;

namespace GDShrapt.LSP.Tests;

[TestClass]
public class GDFoldingRangeHandlerTests
{
    private static string TestProjectPath()
    {
        var baseDir = System.IO.Directory.GetCurrentDirectory();
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        return System.IO.Path.Combine(root, "testproject", "GDShrapt.TestProject");
    }

    private static GDLspFoldingRangeHandler Setup()
    {
        var context = new GDDefaultProjectContext(TestProjectPath());
        var project = new GDScriptProject(context, new GDScriptProjectOptions());
        project.LoadScripts();
        project.AnalyzeAll();
        var registry = new GDServiceRegistry();
        registry.LoadModules(project, new GDBaseModule());
        return new GDLspFoldingRangeHandler(registry.GetService<IGDFoldingRangeHandler>()!);
    }

    private static GDFoldingRangeParams Params(string scriptName) => new()
    {
        TextDocument = new GDLspTextDocumentIdentifier
        {
            Uri = GDDocumentManager.PathToUri(System.IO.Path.Combine(TestProjectPath(), "test_scripts", scriptName))
        }
    };

    [TestMethod]
    public async Task HandleAsync_ReturnsFoldableRegions_ForClassWithMethods()
    {
        var handler = Setup();
        var result = await handler.HandleAsync(Params("simple_class.gd"), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Length.Should().BeGreaterThan(0, "a class with methods has foldable regions");

        foreach (var r in result)
        {
            r.EndLine.Should().BeGreaterThanOrEqualTo(r.StartLine);
            r.StartLine.Should().BeGreaterThanOrEqualTo(0, "folding ranges are 0-based");
        }
    }

    [TestMethod]
    public async Task HandleAsync_NonExistentFile_ReturnsNull()
    {
        var handler = Setup();
        var @params = new GDFoldingRangeParams
        {
            TextDocument = new GDLspTextDocumentIdentifier { Uri = GDDocumentManager.PathToUri("/nope/missing.gd") }
        };

        var result = await handler.HandleAsync(@params, CancellationToken.None);
        result.Should().BeNull();
    }
}
