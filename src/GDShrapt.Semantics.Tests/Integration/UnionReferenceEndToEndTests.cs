using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using GDShrapt.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GDShrapt.Semantics.Tests;

/// <summary>
/// End-to-end behavior of "union references": a member access on a receiver whose data-flow type
/// is a union of unrelated class types is a SHARED reference across both declarations, carrying the
/// member set in SharedTypes.
///
/// State (verified by these tests):
///  - The SharedTypes plumbing + Union detection are wired end-to-end (GDCrossFileReferenceFinder
///    marks Union and records the member set whenever the receiver's data-flow type IS a union).
///  - The remaining blocker for the CLASS-TYPE case is upstream in flow/type resolution: at the
///    call site the receiver type resolves to the declared/first-assignment type (e.g. EnemyU),
///    NOT the data-flow union {EnemyU, PlayerU} — so there is no union to detect. Surfacing a
///    class-type union at the member-access receiver is the precise next step (Workstream A).
/// </summary>
[TestClass]
public class UnionReferenceEndToEndTests
{
    private const string Enemy = "extends Node\nclass_name EnemyU\nfunc engage():\n    pass\n";
    private const string Player = "extends Node\nclass_name PlayerU\nfunc engage():\n    pass\n";

    [TestMethod]
    public void UnionReceiver_BranchMerge_ProducesUnionReferenceWithSharedTypes()
    {
        var refs = CollectEngageRefs(
            "    var x = EnemyU.new()\n" +
            "    if flag:\n" +
            "        x = PlayerU.new()\n" +
            "    x.engage()\n");

        var union = refs.UnionReferences
            .Where(r => r.FilePath != null && r.FilePath.Contains("union_caller"))
            .ToList();

        union.Should().HaveCountGreaterThanOrEqualTo(1);
        union[0].Confidence.Should().Be(GDReferenceConfidence.Union);
        union[0].SharedTypes!.Should().Contain("EnemyU").And.Contain("PlayerU");
    }

    [TestMethod]
    public void UntypedReceiver_NoUnion_IsNotAUnionReference()
    {
        // Guard against false positives: a duck-typed call with no data-flow union must NOT be Union.
        var refs = CollectEngageRefs("    p.engage()\n", paramList: "p");

        refs.UnionReferences
            .Where(r => r.FilePath != null && r.FilePath.Contains("union_caller"))
            .Should().BeEmpty("a duck-typed call with no data-flow union must not be marked Union");
    }

    private static GDSymbolReferences CollectEngageRefs(string callerBody, string paramList = "flag")
    {
        var caller = $"extends Node\n\nfunc run({paramList}):\n{callerBody}";
        var temp = CreateTempProject(new[]
        {
            ("enemy_u.gd", Enemy),
            ("player_u.gd", Player),
            ("union_caller.gd", caller),
        });

        try
        {
            using var project = GDProjectLoader.LoadProject(temp);
            project.BuildCallSiteRegistry();
            var projectModel = new GDProjectSemanticModel(project);

            var enemyPath = project.ScriptFiles.First(f => f.FullPath != null && f.FullPath.Contains("enemy_u")).FullPath;
            var collector = new GDSymbolReferenceCollector(project, projectModel);
            return collector.CollectReferences("engage", enemyPath);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    private static string CreateTempProject((string name, string content)[] scripts)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "gdshrapt_union_e2e_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        File.WriteAllText(Path.Combine(tempPath, "project.godot"),
            "config_version=5\n\n[application]\nconfig/name=\"UnionE2E\"\n");
        foreach (var (name, content) in scripts)
            File.WriteAllText(Path.Combine(tempPath, name), content);
        return tempPath;
    }
}
