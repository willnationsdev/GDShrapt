using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using GDShrapt.Abstractions;
using GDShrapt.Reader;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GDShrapt.Semantics.Tests;

/// <summary>
/// Completeness gate for data-flow type tracing: EVERY concrete GDExpression construct must
/// either produce a non-Variant type (it is genuinely "handled" by the analyzer) or be listed
/// in an explicit, reasoned whitelist of constructs that carry no data type.
///
/// This is the enforcement mechanism for "tracing supports any code construct" — adding a new
/// expression node to the Reader without coverage here fails <see cref="EveryConcreteExpressionType_IsCoveredOrWhitelisted"/>.
/// </summary>
[TestClass]
public class ExpressionTypeCoverageTests
{
    public sealed record Case(string Id, string Expr, Type Focus, string? Expected);

    // Source of truth: one row per data-bearing expression construct.
    // Expected == null means "must be non-Variant" (provider-dependent exact type not pinned).
    private static readonly Case[] Cases =
    {
        new("int-literal",     "42",                          typeof(GDNumberExpression),               "int"),
        new("float-literal",   "3.14",                        typeof(GDNumberExpression),               "float"),
        new("string-literal",  "\"x\"",                       typeof(GDStringExpression),               "String"),
        new("stringname",      "&\"x\"",                      typeof(GDStringNameExpression),           "StringName"),
        new("raw-string",      "r\"x\"",                      typeof(GDRawStringExpression),            "String"),
        new("bool-literal",    "true",                        typeof(GDBoolExpression),                 "bool"),
        new("array-literal",   "[1, 2]",                      typeof(GDArrayInitializerExpression),     null),
        new("dict-literal",    "{\"a\": 1}",                  typeof(GDDictionaryInitializerExpression),null),
        new("identifier",      "arr",                         typeof(GDIdentifierExpression),           null),
        new("member-access",   "n2d.position",                typeof(GDMemberOperatorExpression),       "Vector2"),
        new("call",            "arr.size()",                  typeof(GDCallExpression),                 "int"),
        new("indexer",         "arr[0]",                      typeof(GDIndexerExpression),              "int"),
        new("binary-op",       "1 + 2",                       typeof(GDDualOperatorExpression),         "int"),
        new("as-cast",         "n2d as Sprite2D",             typeof(GDDualOperatorExpression),         null),
        new("is-check",        "n2d is Node",                 typeof(GDDualOperatorExpression),         "bool"),
        new("unary-neg",       "-arr.size()",                 typeof(GDSingleOperatorExpression),       "int"),
        new("unary-not",       "not true",                    typeof(GDSingleOperatorExpression),       "bool"),
        new("ternary",         "1 if true else 2",            typeof(GDIfExpression),                   "int"),
        new("bracket",         "(1 + 2)",                     typeof(GDBracketExpression),              "int"),
        new("get-node",        "$Child",                      typeof(GDGetNodeExpression),              null),
        new("get-unique-node", "%Child",                      typeof(GDGetUniqueNodeExpression),        null),
        new("node-path",       "^\"a/b\"",                    typeof(GDNodePathExpression),             "NodePath"),
        new("lambda",          "func(x): return x",           typeof(GDMethodExpression),               null),
        new("await",           "await _co()",                 typeof(GDAwaitExpression),                "int"),
    };

    // Constructs that legitimately carry no data type (control-flow / statement-like expressions).
    private static readonly HashSet<string> Whitelist = new()
    {
        nameof(GDPassExpression),
        nameof(GDBreakExpression),
        nameof(GDContinueExpression),
        nameof(GDReturnExpression),
        nameof(GDBreakPointExpression),
        nameof(GDYieldExpression),          // legacy GDScript 1.x coroutine keyword
        nameof(GDRestExpression),           // variadic '...' fragment, not a value
        nameof(GDMatchDefaultOperatorExpression), // '_' wildcard in match
        nameof(GDMatchCaseVariableExpression),    // binding occurrence; typed via match-subject elsewhere
    };

    public static IEnumerable<object[]> CaseData => Cases.Select(c => new object[] { c });

    [TestMethod]
    [DynamicData(nameof(CaseData))]
    public void Construct_ProducesNonVariantType(Case c)
    {
        var model = CreateModel($"    var __r = {c.Expr}\n");
        var probe = model.ScriptFile.Class!.Methods.First(m => m.Identifier?.Sequence == "_probe");
        var node = probe.AllNodes.FirstOrDefault(n => n.GetType() == c.Focus);
        node.Should().NotBeNull($"snippet for '{c.Id}' should contain a {c.Focus.Name}");

        var type = model.TypeSystem.GetType((GDExpression)node!);
        type.Should().NotBeNull($"'{c.Id}' should resolve a type");

        var name = type!.DisplayName;
        name.Should().NotBe("Variant", $"construct '{c.Id}' must be traced to a concrete type, not Variant");
        name.Should().NotBeNullOrEmpty();

        if (c.Expected != null)
            name.Should().Be(c.Expected, $"'{c.Id}' should infer {c.Expected}");
    }

    [TestMethod]
    [DynamicData(nameof(CaseData))]
    public void Construct_AttachesProvenanceOrigin(Case c)
    {
        var model = CreateModel($"    var __v = {c.Expr}\n    var __sink = __v\n");
        var probe = model.ScriptFile.Class!.Methods.First(m => m.Identifier?.Sequence == "_probe");

        var sink = probe.AllNodes.OfType<GDVariableDeclarationStatement>()
            .First(v => v.Identifier?.Sequence == "__sink");
        var use = sink.Initializer; // the read of __v
        use.Should().NotBeNull();

        var flow = model.GetVariableTypeAt("__v", use!);
        flow.Should().NotBeNull($"'{c.Id}' should produce a flow type for __v");

        var origins = flow!.CurrentType.GetAllOrigins().SelectMany(o => o.Origins).ToList();
        origins.Should().NotBeEmpty($"construct '{c.Id}' must attach a data-flow origin (provenance)");
        origins.Should().Contain(o => o.Kind != GDTypeOriginKind.Unknown,
            $"construct '{c.Id}' must record a meaningful origin kind, not only Unknown");
    }

    [TestMethod]
    public void EveryConcreteExpressionType_IsCoveredOrWhitelisted()
    {
        var all = typeof(GDExpression).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(GDExpression)) && !t.IsAbstract)
            .Select(t => t.Name)
            .ToHashSet();

        var covered = Cases.Select(c => c.Focus.Name).ToHashSet();

        var uncovered = all.Where(t => !covered.Contains(t) && !Whitelist.Contains(t)).OrderBy(t => t).ToList();

        uncovered.Should().BeEmpty(
            "every concrete GDExpression must have a type-coverage Case or a Whitelist entry; " +
            $"uncovered: {string.Join(", ", uncovered)}");
    }

    private static GDSemanticModel CreateModel(string probeBody)
    {
        var code =
            "extends Node2D\n" +
            "\n" +
            "var arr: Array[int] = [1]\n" +
            "var n2d: Node2D\n" +
            "\n" +
            "func _co() -> int:\n" +
            "    return 1\n" +
            "\n" +
            "func _probe():\n" +
            probeBody;

        var reference = new GDScriptReference("test://virtual/expr_coverage.gd");
        var scriptFile = new GDScriptFile(reference);
        scriptFile.Reload(code);

        var runtimeProvider = new GDCompositeRuntimeProvider(new GDGodotTypesProvider(), null, null, null);
        scriptFile.Analyze(runtimeProvider);
        return scriptFile.SemanticModel!;
    }
}
