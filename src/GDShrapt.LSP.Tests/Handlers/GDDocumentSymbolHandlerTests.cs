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
public class GDDocumentSymbolHandlerTests
{
    private static string TestProjectPath()
    {
        var baseDir = System.IO.Directory.GetCurrentDirectory();
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        return System.IO.Path.Combine(root, "testproject", "GDShrapt.TestProject");
    }

    private static GDDocumentSymbolHandler Setup()
    {
        var context = new GDDefaultProjectContext(TestProjectPath());
        var project = new GDScriptProject(context, new GDScriptProjectOptions());
        project.LoadScripts();
        project.AnalyzeAll();
        var registry = new GDServiceRegistry();
        registry.LoadModules(project, new GDBaseModule());
        return new GDDocumentSymbolHandler(registry.GetService<IGDSymbolsHandler>()!);
    }

    private static GDDocumentSymbolParams Params(string scriptName) => new()
    {
        TextDocument = new GDLspTextDocumentIdentifier
        {
            Uri = GDDocumentManager.PathToUri(System.IO.Path.Combine(TestProjectPath(), "test_scripts", scriptName))
        }
    };

    private static IEnumerable<string> Flatten(IEnumerable<GDLspDocumentSymbol> symbols)
    {
        foreach (var s in symbols)
        {
            yield return s.Name;
            if (s.Children != null)
                foreach (var n in Flatten(s.Children))
                    yield return n;
        }
    }

    [TestMethod]
    public async Task HandleAsync_ReturnsClassMembers()
    {
        var handler = Setup();
        var result = await handler.HandleAsync(Params("simple_class.gd"), CancellationToken.None);

        result.Should().NotBeNull();
        var names = Flatten(result!).ToList();
        names.Should().Contain("get_info", "document symbols should expose the class methods");
    }

    [TestMethod]
    public async Task HandleAsync_NonExistentFile_ReturnsNull()
    {
        var handler = Setup();
        var @params = new GDDocumentSymbolParams
        {
            TextDocument = new GDLspTextDocumentIdentifier { Uri = GDDocumentManager.PathToUri("/nope/missing.gd") }
        };

        var result = await handler.HandleAsync(@params, CancellationToken.None);
        result.Should().BeNull();
    }
}
