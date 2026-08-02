using System.Linq;
using FluentAssertions;
using GDShrapt.Abstractions;
using GDShrapt.Reader;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GDShrapt.Semantics.Tests;

/// <summary>
/// Documents the provenance model's behavior at resolution fallbacks.
///
/// Provenance is COMPLETE for every resolvable construct (enforced by
/// <see cref="ExpressionTypeCoverageTests"/>'s origin gate — all 24 constructs attach an origin).
/// When a value's type genuinely cannot be resolved (an inference cycle, a depth limit), the
/// model represents it as the "unknown" type (Variant). By design (<c>GDUnionType.AddType</c>
/// filters Variant), unknown carries no concrete type entry and therefore no origin — you cannot
/// have "the provenance of an unknown type". The guarantee is: such cases degrade cleanly to
/// Variant rather than crashing or mis-typing.
/// </summary>
[TestClass]
public class ProvenanceFallbackTests
{
    [TestMethod]
    public void UnresolvedCyclicCall_DegradesToVariantCleanly()
    {
        var flow = AnalyzeVar(
            "func _a(): return _b()\n" +
            "func _b(): return _a()\n" +
            "func _probe():\n" +
            "    var x = _a()\n" +
            "    var __here = x\n", "x");

        flow.Should().NotBeNull("x must still have a flow entry");
        flow!.EffectiveType?.DisplayName.Should().Be("Variant",
            "an inference cycle resolves to the 'unknown' type cleanly");
        flow.CurrentType.HasOrigins.Should().BeFalse(
            "by design, the unknown (Variant) type carries no concrete origin");
    }

    private static GDFlowVariableType? AnalyzeVar(string body, string varName)
    {
        var code = "extends Node\n\n" + body;

        var reference = new GDScriptReference("test://virtual/provenance_fallback.gd");
        var scriptFile = new GDScriptFile(reference);
        scriptFile.Reload(code);
        scriptFile.Analyze(new GDCompositeRuntimeProvider(new GDGodotTypesProvider(), null, null, null));
        var model = scriptFile.SemanticModel!;

        var probe = model.ScriptFile.Class!.Methods.First(m => m.Identifier?.Sequence == "_probe");
        var sink = probe.AllNodes.OfType<GDVariableDeclarationStatement>()
            .First(v => v.Identifier?.Sequence == "__here");
        return model.GetVariableTypeAt(varName, sink.Initializer!);
    }
}
