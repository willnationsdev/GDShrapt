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
public class GDDocumentLinkHandlerTests
{
    private static string TestProjectPath()
    {
        var baseDir = System.IO.Directory.GetCurrentDirectory();
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        return System.IO.Path.Combine(root, "testproject", "GDShrapt.TestProject");
    }

    private static GDLspDocumentLinkHandler Setup()
    {
        var context = new GDDefaultProjectContext(TestProjectPath());
        var project = new GDScriptProject(context, new GDScriptProjectOptions());
        project.LoadScripts();
        var registry = new GDServiceRegistry();
        registry.LoadModules(project, new GDBaseModule());
        return new GDLspDocumentLinkHandler(registry.GetService<IGDDocumentLinkHandler>()!);
    }

    private static GDDocumentLinkParams Params(string scriptName) => new()
    {
        TextDocument = new GDLspTextDocumentIdentifier
        {
            Uri = GDDocumentManager.PathToUri(System.IO.Path.Combine(TestProjectPath(), "test_scripts", scriptName))
        }
    };

    [TestMethod]
    public async Task DocumentLinks_PreloadStatements_ReturnLinksToFiles()
    {
        // navigation_test.gd contains: const SCENE = preload("res://test_scenes/player.tscn")
        var result = await Setup().HandleAsync(Params("navigation_test.gd"), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Length.Should().BeGreaterThan(0, "preload statements should produce document links");
        result.Should().Contain(l => l.Target != null && l.Target.Contains("player.tscn"),
            "the preload of player.tscn should be linked");
        result.All(l => l.Range != null && l.Target != null).Should().BeTrue("links must have a range and target");
    }

    [TestMethod]
    public async Task DocumentLinks_NonExistentFile_ReturnsNull()
    {
        var result = await Setup().HandleAsync(
            new GDDocumentLinkParams
            {
                TextDocument = new GDLspTextDocumentIdentifier { Uri = GDDocumentManager.PathToUri("/nope/missing.gd") }
            },
            CancellationToken.None);

        result.Should().BeNull();
    }
}
