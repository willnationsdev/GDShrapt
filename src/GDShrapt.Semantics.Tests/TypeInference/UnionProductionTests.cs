using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using GDShrapt.Abstractions;
using GDShrapt.Reader;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GDShrapt.Semantics.Tests;

/// <summary>
/// Verifies "union production": when data of different (unrelated) types reaches one variable,
/// the flow type must PRESERVE both concrete types as a union, not collapse to Variant. This is
/// the prerequisite for complete union references — a union reference can only exist where the
/// receiver's data-type is a real union.
/// </summary>
[TestClass]
public class UnionProductionTests
{
    [TestMethod]
    public void BranchMerge_DifferentTypes_BuildsUnion()
    {
        var name = VarTypeName(
            "func _probe(flag):\n" +
            "    var x = 1\n" +
            "    if flag:\n" +
            "        x = \"a\"\n" +
            "    var __here = x\n", "x");

        name.Should().Contain("int").And.Contain("String",
            "merging an int fall-through with a String branch must yield a union, got: " + name);
    }

    [TestMethod]
    public void Ternary_DifferentTypes_BuildsUnion()
    {
        var name = VarTypeName(
            "func _probe(flag):\n" +
            "    var x = 1 if flag else \"a\"\n" +
            "    var __here = x\n", "x");

        name.Should().Contain("int").And.Contain("String",
            "a ternary with int/String branches must yield a union, got: " + name);
    }

    [TestMethod]
    public void MultipleAssignments_DifferentTypes_BuildsUnion()
    {
        var name = VarTypeName(
            "func _probe():\n" +
            "    var x = 1\n" +
            "    x = \"a\"\n" +
            "    var __here = x\n", "x");

        // After two assignments the variable's accumulated profile is a union of both.
        name.Should().ContainAny("int", "String");
        name.Should().Contain("String", "the latest assignment is String; the union must keep it, got: " + name);
    }

    [TestMethod]
    public void BranchMerge_UnionMembers_HaveOrigins()
    {
        // The "with per-member origins" guarantee: every union member carries a typed provenance origin
        // (Kind != Unknown), so the data-flow union is traceable, not anonymous.
        var code =
            "extends Node\n\n" +
            "func _probe(flag):\n" +
            "    var x = 1\n" +
            "    if flag:\n" +
            "        x = \"a\"\n" +
            "    var __here = x\n";

        var reference = new GDScriptReference("test://virtual/union_origins.gd");
        var scriptFile = new GDScriptFile(reference);
        scriptFile.Reload(code);
        scriptFile.Analyze(new GDCompositeRuntimeProvider(new GDGodotTypesProvider(), null, null, null));
        var model = scriptFile.SemanticModel!;

        var probe = model.ScriptFile.Class!.Methods.First(m => m.Identifier?.Sequence == "_probe");
        var sink = probe.AllNodes.OfType<GDVariableDeclarationStatement>().First(v => v.Identifier?.Sequence == "__here");
        var flow = model.GetVariableTypeAt("x", sink.Initializer!);

        flow.Should().NotBeNull();
        flow!.CurrentType.IsUnion.Should().BeTrue("an int fall-through merged with a String branch is a union");

        foreach (var member in flow.CurrentType.Types.Where(t => !t.IsVariant))
        {
            var origins = flow.CurrentType.GetOrigins(member);
            origins.Should().NotBeEmpty($"union member '{member.DisplayName}' must carry a provenance origin");
            origins.Should().Contain(o => o.Kind != GDTypeOriginKind.Unknown,
                $"union member '{member.DisplayName}' must have a typed (non-Unknown) origin");
        }
    }

    [TestMethod]
    public void CallSiteParameter_DifferentTypes_BuildsUnion()
    {
        // Call-site parameter union: an untyped parameter whose callers pass different concrete types
        // surfaces a data-flow union in the flow (lazily, from call sites). Prerequisite for union
        // references on parameters.
        var temp = Path.Combine(Path.GetTempPath(), "gdshrapt_uprod_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        File.WriteAllText(Path.Combine(temp, "project.godot"), "config_version=5\n\n[application]\nconfig/name=\"x\"\n");
        File.WriteAllText(Path.Combine(temp, "a.gd"), "class_name UA\nextends Node2D\n\nfunc go() -> void:\n\tpass\n");
        File.WriteAllText(Path.Combine(temp, "b.gd"), "class_name UB\nextends Resource\n\nfunc go() -> void:\n\tpass\n");
        File.WriteAllText(Path.Combine(temp, "bridge.gd"), "class_name UBridge\nextends Node\n\nfunc run(target):\n\ttarget.go()\n");
        File.WriteAllText(Path.Combine(temp, "caller.gd"),
            "extends Node\n\nfunc _ready():\n\tvar r: UBridge = UBridge.new()\n\tr.run(UA.new())\n\tr.run(UB.new())\n");

        try
        {
            using var project = GDProjectLoader.LoadProject(temp);
            project.BuildCallSiteRegistry();
            var projectModel = new GDProjectSemanticModel(project);

            var bridge = project.ScriptFiles.First(f => f.FullPath != null && f.FullPath.Contains("bridge"));
            var model = projectModel.GetSemanticModel(bridge)!;
            var goCall = model.ScriptFile.Class!.AllNodes.OfType<GDMemberOperatorExpression>()
                .First(m => m.Identifier?.Sequence == "go");

            var flow = model.GetVariableTypeAt("target", goCall);

            flow.Should().NotBeNull();
            var members = flow!.CurrentType.Types.Select(t => t.DisplayName).ToList();
            members.Should().Contain("UA").And.Contain("UB",
                "an untyped parameter called with UA and UB must surface the data-flow union {UA, UB}, got: " + string.Join("|", members));
        }
        finally { try { Directory.Delete(temp, recursive: true); } catch { } }
    }

    private static string VarTypeName(string body, string varName)
    {
        var code = "extends Node\n\n" + body;

        var reference = new GDScriptReference("test://virtual/union_production.gd");
        var scriptFile = new GDScriptFile(reference);
        scriptFile.Reload(code);
        scriptFile.Analyze(new GDCompositeRuntimeProvider(new GDGodotTypesProvider(), null, null, null));
        var model = scriptFile.SemanticModel!;

        var probe = model.ScriptFile.Class!.Methods.First(m => m.Identifier?.Sequence == "_probe");
        var sink = probe.AllNodes.OfType<GDVariableDeclarationStatement>()
            .First(v => v.Identifier?.Sequence == "__here");
        var flow = model.GetVariableTypeAt(varName, sink.Initializer!);
        if (flow == null)
            return "<null>";

        // Inspect the raw flow union (CurrentType) — the exact path union references read,
        // rather than EffectiveType which may collapse to a common base for display.
        var members = flow.CurrentType.Types
            .SelectMany(t => t is GDUnionSemanticType us ? us.Types.Select(x => x.DisplayName) : new[] { t.DisplayName });
        return string.Join("|", members);
    }
}
