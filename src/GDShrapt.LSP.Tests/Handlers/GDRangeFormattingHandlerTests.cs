using System.Threading;
using System.Threading.Tasks;
using GDShrapt.Abstractions;
using GDShrapt.CLI.Core;
using GDShrapt.Semantics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FluentAssertions;

namespace GDShrapt.LSP.Tests;

[TestClass]
public class GDRangeFormattingHandlerTests
{
    private static GDRangeFormattingHandler Setup()
    {
        var baseDir = System.IO.Directory.GetCurrentDirectory();
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        var projPath = System.IO.Path.Combine(root, "testproject", "GDShrapt.TestProject");
        var project = new GDScriptProject(new GDDefaultProjectContext(projPath), new GDScriptProjectOptions());
        project.LoadScripts();
        var registry = new GDServiceRegistry();
        registry.LoadModules(project, new GDBaseModule());
        return new GDRangeFormattingHandler(registry.GetService<IGDFormatHandler>()!);
    }

    private static GDDocumentRangeFormattingParams Params(string uri) => new()
    {
        TextDocument = new GDLspTextDocumentIdentifier { Uri = uri },
        Range = new GDLspRange(1, 0, 1, 20),
        Options = new GDFormattingOptions { TabSize = 4, InsertSpaces = false }
    };

    [TestMethod]
    public async Task RangeFormatting_BadlyIndentedFile_ReturnsChangingEdit()
    {
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "gdshrapt_rangefmt_" + System.Guid.NewGuid().ToString("N") + ".gd");
        // Body indented with 8 spaces; with InsertSpaces=false the formatter normalizes to a tab.
        System.IO.File.WriteAllText(tmp, "func foo():\n        var x = 1\n");
        try
        {
            var result = await Setup().HandleAsync(Params(GDDocumentManager.PathToUri(tmp)), CancellationToken.None);

            result.Should().NotBeNull();
            result!.Length.Should().Be(1);
            result[0].NewText.Should().NotBe(System.IO.File.ReadAllText(tmp),
                "range formatting should produce a changing edit for badly-formatted code");
            result[0].NewText.Should().Contain("\tvar x", "indentation should be normalized to a tab");
        }
        finally
        {
            System.IO.File.Delete(tmp);
        }
    }

    [TestMethod]
    public async Task RangeFormatting_AlreadyFormatted_ReturnsEmpty()
    {
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "gdshrapt_rangefmt_" + System.Guid.NewGuid().ToString("N") + ".gd");
        System.IO.File.WriteAllText(tmp, "func foo():\n\tvar x = 1\n");
        try
        {
            var result = await Setup().HandleAsync(Params(GDDocumentManager.PathToUri(tmp)), CancellationToken.None);
            result.Should().NotBeNull();
            result!.Should().BeEmpty("an already-formatted document yields no edits");
        }
        finally
        {
            System.IO.File.Delete(tmp);
        }
    }
}
