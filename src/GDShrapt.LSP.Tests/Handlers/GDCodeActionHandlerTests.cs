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
public class GDCodeActionHandlerTests
{
    private static string TestProjectPath()
    {
        var baseDir = System.IO.Directory.GetCurrentDirectory();
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        return System.IO.Path.Combine(root, "testproject", "GDShrapt.TestProject");
    }

    private static GDLspCodeActionHandler Setup()
    {
        var context = new GDDefaultProjectContext(TestProjectPath());
        var project = new GDScriptProject(context, new GDScriptProjectOptions());
        project.LoadScripts();
        project.AnalyzeAll();
        var registry = new GDServiceRegistry();
        registry.LoadModules(project, new GDBaseModule());
        return new GDLspCodeActionHandler(registry.GetService<IGDCodeActionHandler>()!);
    }

    private static GDCodeActionParams Params(string scriptName, int startLine, int endLine) => new()
    {
        TextDocument = new GDLspTextDocumentIdentifier
        {
            Uri = GDDocumentManager.PathToUri(System.IO.Path.Combine(TestProjectPath(), "test_scripts", scriptName))
        },
        Range = new GDLspRange(startLine, 0, endLine, 0)
    };

    [TestMethod]
    public async Task HandleAsync_OverFile_DoesNotThrow_AndActionsAreWellFormed()
    {
        var handler = Setup();
        var result = await handler.HandleAsync(Params("simple_class.gd", 0, 60), CancellationToken.None);

        // Code actions depend on diagnostics in range; contract: never throws, results well-formed.
        if (result != null)
        {
            foreach (var action in result)
                action.Title.Should().NotBeNullOrEmpty();
        }
    }

    [TestMethod]
    public async Task HandleAsync_NonExistentFile_ReturnsNull()
    {
        var handler = Setup();
        var @params = new GDCodeActionParams
        {
            TextDocument = new GDLspTextDocumentIdentifier { Uri = GDDocumentManager.PathToUri("/nope/missing.gd") },
            Range = new GDLspRange(0, 0, 1, 0)
        };

        var result = await handler.HandleAsync(@params, CancellationToken.None);
        result.Should().BeNull();
    }
}
