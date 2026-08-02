using System.Linq;
using FluentAssertions;
using GDShrapt.Reader;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GDShrapt.Semantics.Tests;

/// <summary>
/// Verifies data tracing through type-narrowing control-flow constructs: an untyped parameter
/// must be narrowed (its flow type refined) inside the guarded scope for `is`, `assert(... is ...)`
/// and `typeof(...) == TYPE_*`. These are the constructs an earlier audit claimed were missing —
/// this pins the real behavior.
/// </summary>
[TestClass]
public class NarrowingCoverageTests
{
    [TestMethod]
    public void IsCheck_NarrowsParameterInBranch()
    {
        var t = NarrowedParamType(
            "    if p is Node2D:\n" +
            "        var __here = p\n");
        t.Should().Be("Node2D", "`if p is Node2D:` must narrow p to Node2D inside the branch");
    }

    [TestMethod]
    public void Assert_NarrowsParameterAfterAssert()
    {
        var t = NarrowedParamType(
            "    assert(p is Node2D)\n" +
            "    var __here = p\n");
        t.Should().Be("Node2D", "`assert(p is Node2D)` must narrow p to Node2D after the assert");
    }

    [TestMethod]
    public void TypeOf_NarrowsParameterInBranch()
    {
        var t = NarrowedParamType(
            "    if typeof(p) == TYPE_INT:\n" +
            "        var __here = p\n");
        t.Should().Be("int", "`if typeof(p) == TYPE_INT:` must narrow p to int inside the branch");
    }

    private static string? NarrowedParamType(string body)
    {
        var code =
            "extends Node\n" +
            "\n" +
            "func _probe(p):\n" +
            body;

        var reference = new GDScriptReference("test://virtual/narrowing.gd");
        var scriptFile = new GDScriptFile(reference);
        scriptFile.Reload(code);
        scriptFile.Analyze(new GDCompositeRuntimeProvider(new GDGodotTypesProvider(), null, null, null));
        var model = scriptFile.SemanticModel!;

        var probe = model.ScriptFile.Class!.Methods.First(m => m.Identifier?.Sequence == "_probe");
        var sink = probe.AllNodes.OfType<GDVariableDeclarationStatement>()
            .First(v => v.Identifier?.Sequence == "__here");
        var pRead = sink.Initializer;

        var flow = model.GetVariableTypeAt("p", pRead!);
        return flow?.EffectiveType?.DisplayName;
    }
}
