using GDShrapt.Converter;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GDShrapt.Converter.Tests;

[TestClass]
public sealed class GDConversionPipelineTests
{
    private string _projectDirectory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _projectDirectory = Path.Combine(Path.GetTempPath(), "GDShrapt.Converter.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectDirectory);
        File.WriteAllText(Path.Combine(_projectDirectory, "alpha.gd"), "class_name Alpha\nextends RefCounted\nvar peer: Beta\n");
        File.WriteAllText(Path.Combine(_projectDirectory, "beta.gd"), "class_name Beta\nextends RefCounted\nvar peer: Alpha\n");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_projectDirectory))
            Directory.Delete(_projectDirectory, recursive: true);
    }

    [TestMethod]
    public async Task AnalyzeAsync_LoadsAllScriptsBeforeSemanticAnalysis()
    {
        using var analysis = await GDConversionAnalyzer.AnalyzeAsync(
            _projectDirectory,
            new GDConversionAnalysisOptions { MaxDegreeOfParallelism = 1, EnrichCallSites = false });

        Assert.AreEqual(2, analysis.Scripts.Count);
        Assert.IsTrue(analysis.Scripts.All(script => script.Class != null));
        Assert.IsTrue(analysis.Scripts.All(script => script.SemanticModel != null));
        CollectionAssert.AreEquivalent(new[] { "Alpha", "Beta" }, analysis.Scripts.Select(script => script.TypeName).ToArray());
    }

    [TestMethod]
    public async Task AnalyzeAsync_FocusPathLimitsSemanticAnalysisToDirectoryTree()
    {
        var focusDirectory = Path.Combine(_projectDirectory, "scripts", "focus");
        var nestedDirectory = Path.Combine(focusDirectory, "nested");
        var outsideDirectory = Path.Combine(_projectDirectory, "scripts", "other");
        Directory.CreateDirectory(nestedDirectory);
        Directory.CreateDirectory(outsideDirectory);
        File.WriteAllText(
            Path.Combine(focusDirectory, "target.gd"),
            "class_name Target\nextends RefCounted\nfunc accept(value):\n\tpass\nfunc call_target():\n\taccept(1)\n");
        File.WriteAllText(
            Path.Combine(nestedDirectory, "in_scope_caller.gd"),
            "class_name InScopeCaller\nextends RefCounted\nfunc call_target():\n\tpass\n");
        File.WriteAllText(
            Path.Combine(outsideDirectory, "out_of_scope_caller.gd"),
            "class_name OutOfScopeCaller\nextends RefCounted\nfunc call_target(target: Target):\n\ttarget.accept(\"outside\")\n");

        var analyzedFileCount = 0;
        using var analysis = await GDConversionAnalyzer.AnalyzeAsync(
            _projectDirectory,
            new GDConversionAnalysisOptions
            {
                FocusPath = Path.Combine("scripts", "focus"),
                MaxDegreeOfParallelism = 1,
                ProgressStarting = count => analyzedFileCount = count
            });

        Assert.AreEqual(5, analysis.Scripts.Count);
        Assert.AreEqual(2, analyzedFileCount);
        var target = analysis.Scripts.Single(script => script.TypeName == "Target");
        var inScopeCaller = analysis.Scripts.Single(script => script.TypeName == "InScopeCaller");
        var outOfScopeCaller = analysis.Scripts.Single(script => script.TypeName == "OutOfScopeCaller");
        Assert.IsNotNull(target.SemanticModel);
        Assert.IsNotNull(inScopeCaller.SemanticModel);
        Assert.IsNull(outOfScopeCaller.SemanticModel);
    }

    [TestMethod]
    public async Task CreatePlan_SelectsHighestPriorityMatchingRule()
    {
        using var analysis = await CreateAnalysisAsync();
        var service = new GDConversionService();
        var rules = new GDConversionRuleSet(new IGDConversionRule[]
        {
            new TestRule("first", 10),
            new TestRule("preferred", 20)
        });

        var plan = service.CreatePlan(analysis, rules);

        Assert.IsTrue(plan.Entries.All(entry => entry.SelectedRule == "preferred"));
        Assert.IsTrue(plan.Entries.All(entry => entry.MatchingRules.SequenceEqual(new[] { "preferred", "first" })));
        Assert.AreEqual(2, plan.Outputs.Count());
    }

    [TestMethod]
    public async Task WriteOutputs_RejectsPathsOutsideOutputDirectory()
    {
        using var analysis = await CreateAnalysisAsync();
        var service = new GDConversionService();
        var plan = service.CreatePlan(analysis, new GDConversionRuleSet(new[] { new TestRule("escape", 1, "../{type}.cs") }));
        var outputDirectory = Path.Combine(_projectDirectory, "generated");

        Assert.ThrowsException<InvalidOperationException>(() => service.WriteOutputs(plan, outputDirectory));
        Assert.IsFalse(File.Exists(Path.Combine(_projectDirectory, "escape.cs")));
    }

    [TestMethod]
    public async Task WriteOutputs_ValidatesAllPathsBeforeWriting()
    {
        using var analysis = await CreateAnalysisAsync();
        var service = new GDConversionService();
        var plan = service.CreatePlan(analysis, new GDConversionRuleSet(new[] { new MixedOutputRule() }));
        var outputDirectory = Path.Combine(_projectDirectory, "generated");

        Assert.ThrowsException<InvalidOperationException>(() => service.WriteOutputs(plan, outputDirectory));
        Assert.IsFalse(File.Exists(Path.Combine(outputDirectory, "alpha.cs")));
    }

    private Task<GDConversionAnalysis> CreateAnalysisAsync()
    {
        return GDConversionAnalyzer.AnalyzeAsync(
            _projectDirectory,
            new GDConversionAnalysisOptions { MaxDegreeOfParallelism = 1, EnrichCallSites = false });
    }

    private sealed class TestRule(string name, int priority, string? relativePath = null) : IGDConversionRule
    {
        public string Name { get; } = name;
        public int Priority { get; } = priority;

        public bool CanConvert(GDConversionContext context) => true;

        public IEnumerable<GDConversionOutput> Convert(GDConversionContext context)
        {
            var outputPath = relativePath?.Replace("{type}", context.Script.TypeName ?? "script")
                ?? $"{context.Script.TypeName}.cs";
            return new[] { new GDConversionOutput(outputPath, $"// generated by {Name}") };
        }
    }

    private sealed class MixedOutputRule : IGDConversionRule
    {
        public string Name => "mixed";
        public int Priority => 1;
        public bool CanConvert(GDConversionContext context) => true;

        public IEnumerable<GDConversionOutput> Convert(GDConversionContext context)
        {
            var outputPath = context.Script.TypeName == "Alpha" ? "alpha.cs" : "../escape.cs";
            return new[] { new GDConversionOutput(outputPath, "// generated") };
        }
    }
}