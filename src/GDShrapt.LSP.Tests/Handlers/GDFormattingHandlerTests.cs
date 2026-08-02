using System.Threading;
using System.Threading.Tasks;
using GDShrapt.Abstractions;
using GDShrapt.CLI.Core;
using GDShrapt.Semantics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FluentAssertions;

namespace GDShrapt.LSP.Tests;

[TestClass]
public class GDFormattingHandlerTests
{
    private static string TestProjectPath()
    {
        var baseDir = System.IO.Directory.GetCurrentDirectory();
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        return System.IO.Path.Combine(root, "testproject", "GDShrapt.TestProject");
    }

    private static GDFormattingHandler Setup()
    {
        var context = new GDDefaultProjectContext(TestProjectPath());
        var project = new GDScriptProject(context, new GDScriptProjectOptions());
        project.LoadScripts();
        project.AnalyzeAll();
        var registry = new GDServiceRegistry();
        registry.LoadModules(project, new GDBaseModule());
        return new GDFormattingHandler(registry.GetService<IGDFormatHandler>()!);
    }

    private static GDDocumentFormattingParams Params(string scriptName) => new()
    {
        TextDocument = new GDLspTextDocumentIdentifier
        {
            Uri = GDDocumentManager.PathToUri(System.IO.Path.Combine(TestProjectPath(), "test_scripts", scriptName))
        },
        Options = new GDFormattingOptions { TabSize = 4, InsertSpaces = false }
    };

    [TestMethod]
    public async Task HandleAsync_ValidFile_ReturnsEditsOrEmpty_WholeDocumentRange()
    {
        var handler = Setup();
        var result = await handler.HandleAsync(Params("simple_class.gd"), CancellationToken.None);

        // A valid, loaded file always yields a (possibly empty) edit array, never null.
        result.Should().NotBeNull();

        // If the file needed reformatting, the edit replaces the whole document from (0,0).
        if (result!.Length > 0)
        {
            result.Should().HaveCount(1, "formatting returns a single whole-document edit");
            result[0].Range.Start.Line.Should().Be(0);
            result[0].Range.Start.Character.Should().Be(0);
        }
    }

    [TestMethod]
    public async Task HandleAsync_NonExistentFile_ReturnsNull()
    {
        var handler = Setup();
        var @params = new GDDocumentFormattingParams
        {
            TextDocument = new GDLspTextDocumentIdentifier { Uri = GDDocumentManager.PathToUri("/nope/missing.gd") },
            Options = new GDFormattingOptions { TabSize = 4, InsertSpaces = false }
        };

        var result = await handler.HandleAsync(@params, CancellationToken.None);
        result.Should().BeNull();
    }
}
