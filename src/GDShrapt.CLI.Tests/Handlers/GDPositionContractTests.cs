using System.Collections.Generic;
using System.Linq;
using GDShrapt.Abstractions;
using GDShrapt.CLI.Core;
using GDShrapt.Semantics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FluentAssertions;

namespace GDShrapt.CLI.Tests.Handlers;

/// <summary>
/// Pins the CLI.Core output position contract so it cannot drift silently.
///
/// IMPORTANT: the column base is currently NOT uniform across CLI.Core DTOs (a known,
/// non-user-visible inconsistency — each LSP/Plugin consumer compensates):
///   - GDCliReferenceLocation.Column is 1-based
///   - GDDefinitionLocation.Column   is 0-based
/// Line is 1-based for both. Diagnostics and semantic tokens use their own AST-aligned
/// (0-based column) convention and are out of scope here.
///
/// If a future change normalizes these (e.g. makes GDDefinitionLocation 1-based), this test
/// must be updated deliberately — and every LSP/Plugin consumer reviewed in the same change.
/// </summary>
[TestClass]
public class GDPositionContractTests
{
    private static string TestProjectPath()
    {
        var baseDir = System.IO.Directory.GetCurrentDirectory();
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        return System.IO.Path.Combine(root, "testproject", "GDShrapt.TestProject");
    }

    private static (IGDFindRefsHandler refs, IGDGoToDefHandler def) Setup()
    {
        var context = new GDDefaultProjectContext(TestProjectPath());
        var project = new GDScriptProject(context, new GDScriptProjectOptions());
        project.LoadScripts();
        project.AnalyzeAll();

        var registry = new GDServiceRegistry();
        registry.LoadModules(project, new GDBaseModule());
        return (registry.GetService<IGDFindRefsHandler>()!, registry.GetService<IGDGoToDefHandler>()!);
    }

    private static IEnumerable<GDCliReferenceLocation> Flatten(IEnumerable<GDReferenceGroup> groups)
    {
        foreach (var g in groups)
        {
            foreach (var l in g.Locations) yield return l;
            foreach (var l in Flatten(g.Overrides)) yield return l;
        }
    }

    [TestMethod]
    public void Columns_ReferenceIs1Based_DefinitionIs0Based_LineIs1BasedForBoth()
    {
        var (refs, def) = Setup();
        var baseEntity = System.IO.Path.Combine(TestProjectPath(), "test_scripts", "base_entity.gd");

        // base_entity.gd line 27 (0-based) declares take_damage; identifier starts at col 5 (0-based).
        var all = Flatten(refs.FindReferences("take_damage", baseEntity)).ToList();
        var decl = all.First(r => r.IsDeclaration);

        // Reference columns are 1-based (0-based 5 → 6), lines 1-based (0-based 27 → 28).
        decl.Line.Should().Be(28);
        decl.Column.Should().Be(6, "GDCliReferenceLocation.Column is 1-based");

        // Resolve a usage back to the declaration to observe the definition convention.
        var usage = all.First(r => !r.IsDeclaration);
        var resolved = def.FindDefinition(usage.FilePath, usage.Line, usage.Column);
        resolved.Should().NotBeNull();
        resolved!.SymbolName.Should().Be("take_damage");

        // Definition column is 0-based: exactly one less than the 1-based reference column.
        resolved.Line.Should().Be(28, "GDDefinitionLocation.Line is 1-based");
        resolved.Column.Should().Be(decl.Column - 1, "GDDefinitionLocation.Column is 0-based (one less than the reference column)");
    }
}
