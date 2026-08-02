using System.IO;
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
public class GDFileRenameHandlerTests
{
    private static (GDScriptProject project, GDFileRenameHandler handler) Setup()
    {
        var baseDir = System.IO.Directory.GetCurrentDirectory();
        var root = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        var projPath = Path.Combine(root, "testproject", "GDShrapt.TestProject");
        var project = new GDScriptProject(new GDDefaultProjectContext(projPath), new GDScriptProjectOptions());
        project.LoadScripts();
        return (project, new GDFileRenameHandler(project));
    }

    [TestMethod]
    public void FileRename_PreloadedFile_UpdatesResReferenceInReferencingScript()
    {
        var (project, handler) = Setup();
        var oldPath = Path.Combine(project.ProjectPath, "test_scenes", "player.tscn");
        var newPath = Path.Combine(project.ProjectPath, "test_scenes", "player2.tscn");

        var edits = handler.GetRenameEdits(oldPath, newPath);

        edits.Should().NotBeEmpty("renaming a preloaded file should update referencing scripts");
        edits.Should().Contain(e =>
            e.FilePath.Contains("navigation_test.gd") &&
            e.NewText == "\"res://test_scenes/player2.tscn\"",
            "the preload(\"res://test_scenes/player.tscn\") should be rewritten to player2.tscn");
    }

    [TestMethod]
    public void FileRename_UnreferencedFile_ReturnsNoEdits()
    {
        var (project, handler) = Setup();
        var oldPath = Path.Combine(project.ProjectPath, "test_scenes", "totally_unreferenced_xyz.tscn");
        var newPath = Path.Combine(project.ProjectPath, "test_scenes", "renamed_xyz.tscn");

        handler.GetRenameEdits(oldPath, newPath).Should().BeEmpty();
    }

    [TestMethod]
    public void FileRename_SamePath_ReturnsNoEdits()
    {
        var (project, handler) = Setup();
        var p = Path.Combine(project.ProjectPath, "test_scenes", "player.tscn");
        handler.GetRenameEdits(p, p).Should().BeEmpty();
    }

    [TestMethod]
    public async Task LspWrapper_RenamePreloadedFile_ReturnsWorkspaceEdit()
    {
        var (project, handler) = Setup();
        var lsp = new GDLspFileRenameHandler(handler);
        var oldUri = GDDocumentManager.PathToUri(Path.Combine(project.ProjectPath, "test_scenes", "player.tscn"));
        var newUri = GDDocumentManager.PathToUri(Path.Combine(project.ProjectPath, "test_scenes", "player2.tscn"));

        var result = await lsp.HandleAsync(
            new GDRenameFilesParams { Files = { new GDFileRename { OldUri = oldUri, NewUri = newUri } } },
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.Changes.Should().NotBeNull();
        result.Changes!.Keys.Should().Contain(k => k.Contains("navigation_test.gd"));
        result.Changes.First(kv => kv.Key.Contains("navigation_test.gd")).Value
            .Should().Contain(e => e.NewText == "\"res://test_scenes/player2.tscn\"");
    }
}
