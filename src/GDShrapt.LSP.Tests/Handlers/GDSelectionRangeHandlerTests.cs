using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GDShrapt.Abstractions;
using GDShrapt.CLI.Core;
using GDShrapt.Semantics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FluentAssertions;

namespace GDShrapt.LSP.Tests;

[TestClass]
public class GDSelectionRangeHandlerTests
{
    private static string TestProjectPath()
    {
        var baseDir = System.IO.Directory.GetCurrentDirectory();
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        return System.IO.Path.Combine(root, "testproject", "GDShrapt.TestProject");
    }

    private static GDLspSelectionRangeHandler Setup()
    {
        var context = new GDDefaultProjectContext(TestProjectPath());
        var project = new GDScriptProject(context, new GDScriptProjectOptions());
        project.LoadScripts();
        project.AnalyzeAll();
        var registry = new GDServiceRegistry();
        registry.LoadModules(project, new GDBaseModule());
        return new GDLspSelectionRangeHandler(registry.GetService<IGDSelectionRangeHandler>()!);
    }

    private static GDSelectionRangeParams Params(string scriptName, int line, int character) => new()
    {
        TextDocument = new GDLspTextDocumentIdentifier
        {
            Uri = GDDocumentManager.PathToUri(System.IO.Path.Combine(TestProjectPath(), "test_scripts", scriptName))
        },
        Positions = new List<GDLspPosition> { new GDLspPosition(line, character) }
    };

    [TestMethod]
    public async Task SelectionRange_OnIdentifier_ReturnsGrowingNestedChain()
    {
        var handler = Setup();
        // signals_test.gd line 39 (1-based): "var old = _health" → "_health" at LSP (38, 11)
        var result = await handler.HandleAsync(Params("signals_test.gd", 38, 11), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Length.Should().Be(1);

        var chain = new List<GDSelectionRange>();
        for (var sr = result[0]; sr != null; sr = sr.Parent)
            chain.Add(sr);

        chain.Count.Should().BeGreaterThan(1, "expand-selection should produce nested ranges from the identifier outward");

        for (int i = 0; i + 1 < chain.Count; i++)
            Contains(chain[i + 1].Range, chain[i].Range).Should().BeTrue("each parent range must contain its child range");

        // Outermost (class) should span more lines than the innermost (identifier).
        var innermost = chain[0].Range;
        var outermost = chain[chain.Count - 1].Range;
        (outermost.End.Line - outermost.Start.Line)
            .Should().BeGreaterThan(innermost.End.Line - innermost.Start.Line);
    }

    [TestMethod]
    public async Task SelectionRange_NonExistentFile_ReturnsZeroWidthAtCursor()
    {
        var handler = Setup();
        var @params = new GDSelectionRangeParams
        {
            TextDocument = new GDLspTextDocumentIdentifier { Uri = GDDocumentManager.PathToUri("/nope/missing.gd") },
            Positions = new List<GDLspPosition> { new GDLspPosition(3, 2) }
        };

        var result = await handler.HandleAsync(@params, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Length.Should().Be(1);
        result[0].Parent.Should().BeNull("no AST → a single zero-width range at the cursor");
        result[0].Range.Start.Line.Should().Be(3);
        result[0].Range.Start.Character.Should().Be(2);
    }

    [TestMethod]
    public async Task SelectionRange_OutOfRangePosition_DoesNotThrow_ReturnsZeroWidth()
    {
        var handler = Setup();
        // Line/column far past EOF → no token resolved → a single zero-width range, no exception.
        var result = await handler.HandleAsync(Params("simple_class.gd", 99999, 99999), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Length.Should().Be(1);
    }

    private static bool Contains(GDLspRange outer, GDLspRange inner)
    {
        var startOk = outer.Start.Line < inner.Start.Line ||
                      (outer.Start.Line == inner.Start.Line && outer.Start.Character <= inner.Start.Character);
        var endOk = outer.End.Line > inner.End.Line ||
                    (outer.End.Line == inner.End.Line && outer.End.Character >= inner.End.Character);
        return startOk && endOk;
    }
}
