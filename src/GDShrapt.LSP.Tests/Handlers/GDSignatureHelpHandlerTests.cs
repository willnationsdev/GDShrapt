using System.Threading;
using System.Threading.Tasks;
using GDShrapt.Abstractions;
using GDShrapt.CLI.Core;
using GDShrapt.Semantics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FluentAssertions;

namespace GDShrapt.LSP.Tests;

[TestClass]
public class GDSignatureHelpHandlerTests
{
    private static string TestProjectPath()
    {
        var baseDir = System.IO.Directory.GetCurrentDirectory();
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        return System.IO.Path.Combine(root, "testproject", "GDShrapt.TestProject");
    }

    private static GDLspSignatureHelpHandler Setup()
    {
        var context = new GDDefaultProjectContext(TestProjectPath());
        var project = new GDScriptProject(context, new GDScriptProjectOptions());
        project.LoadScripts();
        project.AnalyzeAll();
        var registry = new GDServiceRegistry();
        registry.LoadModules(project, new GDBaseModule());
        return new GDLspSignatureHelpHandler(registry.GetService<IGDSignatureHelpHandler>()!);
    }

    private static GDSignatureHelpParams Params(string scriptName, int line, int character) => new()
    {
        TextDocument = new GDLspTextDocumentIdentifier
        {
            Uri = GDDocumentManager.PathToUri(System.IO.Path.Combine(TestProjectPath(), "test_scripts", scriptName))
        },
        Position = new GDLspPosition(line, character)
    };

    [TestMethod]
    public async Task HandleAsync_InsideCall_DoesNotThrow_ReturnsSignaturesWhenAvailable()
    {
        var handler = Setup();

        // Exercise across a method-heavy file; signature help is context dependent, so the
        // contract under test is: never throws, and any result is well-formed.
        var result = await handler.HandleAsync(Params("simple_class.gd", 35, 20), CancellationToken.None);

        if (result != null)
            result.Signatures.Should().NotBeNull();
    }

    [TestMethod]
    public async Task HandleAsync_InsideMethodCall_ReturnsSignatureForCallee()
    {
        var handler = Setup();
        // base_entity.gd line 33 (1-based): "\tvar actual_damage = calculate_actual_damage(amount)"
        // cursor inside the call's parentheses → LSP (32, 46)
        var result = await handler.HandleAsync(Params("base_entity.gd", 32, 46), CancellationToken.None);

        result.Should().NotBeNull("the cursor is inside a call, so a signature should be offered");
        result!.Signatures.Should().NotBeNullOrEmpty();
        result.Signatures.Should().Contain(s => s.Label.Contains("calculate_actual_damage"),
            "the signature for the called method should be shown");
    }

    [TestMethod]
    public async Task HandleAsync_NonExistentFile_ReturnsNull()
    {
        var handler = Setup();
        var @params = new GDSignatureHelpParams
        {
            TextDocument = new GDLspTextDocumentIdentifier { Uri = GDDocumentManager.PathToUri("/nope/missing.gd") },
            Position = new GDLspPosition(0, 0)
        };

        var result = await handler.HandleAsync(@params, CancellationToken.None);
        result.Should().BeNull();
    }
}
