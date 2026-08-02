using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GDShrapt.Semantics.Tests;

/// <summary>
/// Rename treats a union/shared reference as a strict (auto-applied) edit ONLY when every type it is
/// shared across is renamed in the same operation — so all possible resolutions of the receiver yield
/// the identical edit and the rewrite is risk-free. Otherwise it stays potential (shown, never applied).
/// Controlled by <see cref="GDRenameService.PromoteFullyCoveredUnionReferences"/> (default true).
///
/// State: the promotion + flag are wired and safe. The positive case can only fire once a class-type
/// union reference is actually produced at the call site — currently the receiver resolves to a
/// duck-typed (Potential) reference rather than a data-flow union {A, B}, so no SharedTypes are
/// produced to promote (the open union-production gap; see UnionReferenceEndToEndTests).
/// </summary>
[TestClass]
public class UnionRenamePromotionTests
{
    private const string BaseA = "class_name BaseA\nextends Node2D\n\nfunc execute() -> void:\n\tpass\n";
    private const string BaseB = "class_name BaseB\nextends Resource\n\nfunc execute() -> void:\n\tpass\n";
    private const string Bridge = "class_name BridgeRunner\nextends Node\n\nfunc run(target):\n\ttarget.execute()\n";
    private const string Caller =
        "extends Node\n\nfunc _ready():\n\tvar b: BridgeRunner = BridgeRunner.new()\n\tb.run(BaseA.new())\n\tb.run(BaseB.new())\n";

    [TestMethod]
    public void Flag_DefaultsTrue()
    {
        var temp = CreateTempProject(new[] { ("base_a.gd", BaseA) });
        try
        {
            using var project = GDProjectLoader.LoadProject(temp);
            var projectModel = new GDProjectSemanticModel(project);

            var service = new GDRenameService(project, projectModel);
            service.PromoteFullyCoveredUnionReferences.Should().BeTrue("the restored behavior is on by default");
        }
        finally { TryDelete(temp); }
    }

    [TestMethod]
    public void NoCallSites_DuckReceiver_StaysPotential()
    {
        // Safety guard: with NO call sites, the parameter is a genuinely duck-typed receiver (no provable
        // data-flow union), so the shared call must never be auto-applied as strict.
        var result = Rename(promote: true, withCaller: false);

        result.Success.Should().BeTrue();
        BridgeEdits(result.StrictEdits).Should().BeEmpty(
            "a duck-typed receiver with no provable union must not be auto-applied");
        BridgeEdits(result.PotentialEdits).Should().HaveCountGreaterThanOrEqualTo(1,
            "the duck-typed bridge call is shown as a potential edit");
    }

    [TestMethod]
    public void FullyCoveredUnion_SharedCall_IsPromotedToStrict()
    {
        // The bridge parameter is provably {BaseA, BaseB} from its call sites (a data-flow union); the
        // union reference links both hierarchies, so renaming covers both and the shared call is promoted.
        var result = Rename(promote: true);

        BridgeEdits(result.StrictEdits).Should().HaveCountGreaterThanOrEqualTo(1,
            "every type the shared call resolves to is renamed, so the edit is identical and risk-free");

        var disabled = Rename(promote: false);
        BridgeEdits(disabled.StrictEdits).Should().BeEmpty(
            "with the flag disabled the shared call must never be auto-applied");
    }

    private static System.Collections.Generic.IEnumerable<GDTextEdit> BridgeEdits(
        System.Collections.Generic.IEnumerable<GDTextEdit> edits)
        => edits.Where(e => e.FilePath != null && e.FilePath.Replace('\\', '/').EndsWith("bridge.gd"));

    private static GDRenameResult Rename(bool promote, bool withCaller = true)
    {
        var scripts = withCaller
            ? new[] { ("base_a.gd", BaseA), ("base_b.gd", BaseB), ("bridge.gd", Bridge), ("caller.gd", Caller) }
            : new[] { ("base_a.gd", BaseA), ("base_b.gd", BaseB), ("bridge.gd", Bridge) };
        var temp = CreateTempProject(scripts);

        try
        {
            using var project = GDProjectLoader.LoadProject(temp);
            project.BuildCallSiteRegistry();
            var projectModel = new GDProjectSemanticModel(project);

            var baseAPath = project.ScriptFiles
                .First(f => f.FullPath != null && f.FullPath.Replace('\\', '/').EndsWith("base_a.gd")).FullPath;

            var service = new GDRenameService(project, projectModel)
            {
                PromoteFullyCoveredUnionReferences = promote
            };

            return service.PlanRename("execute", "run_now", baseAPath);
        }
        finally { TryDelete(temp); }
    }

    private static string CreateTempProject((string name, string content)[] scripts)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "gdshrapt_union_rename_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        File.WriteAllText(Path.Combine(tempPath, "project.godot"),
            "config_version=5\n\n[application]\nconfig/name=\"UnionRename\"\n");
        foreach (var (name, content) in scripts)
            File.WriteAllText(Path.Combine(tempPath, name), content);
        return tempPath;
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
