using GDShrapt.Converter.Planning;
using GDShrapt.Reader;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
        File.WriteAllText(Path.Combine(_projectDirectory, "alpha.gd"), "class_name Alpha\nextends RefCounted\nvar peer: Beta\nfunc ping():\n\tpass\n");
        File.WriteAllText(Path.Combine(_projectDirectory, "beta.gd"), "class_name Beta\nextends RefCounted\nvar peer: Alpha\nfunc ping():\n\tpass\n");
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
    public async Task CreatePlan_MapsEverySyntaxElementAndAllowsSpecializedRules()
    {
        using var analysis = await CreateAnalysisAsync();
        var rules = new GDConversionRuleSet(
        [
            new IgnoreNodeRule(),
            new ClassMappingRule(),
            new VariableMappingRule(),
            new MethodMappingRule(),
            new StatementsMappingRule(),
            new PassMappingRule()
        ]);

        var service = new GDConversionService();
        var plan = service.CreatePlan(analysis, rules);

        Assert.IsTrue(plan.IsComplete);
        foreach (var entry in plan.Entries)
        {
            var root = analysis.Scripts.Single(script =>
                (script.FullPath ?? script.ResPath ?? script.TypeName ?? "<unknown>") == entry.SourcePath).Class!;
            Assert.AreEqual(1 + root.AllNodes.Count() + root.AllTokens.Count(), entry.Mappings.Count);
            Assert.IsTrue(entry.Mappings.All(mapping =>
                mapping.Result.Disposition != GDConversionNodeDisposition.Unmapped));

            var variableMapping = entry.Mappings.Single(mapping => mapping.Node is GDVariableDeclaration);
            Assert.AreEqual("variable-specialist", variableMapping.RuleName);
            Assert.AreEqual(GDConversionNodeDisposition.Converted, variableMapping.Result.Disposition);
            var peerType = entry.SourcePath.Contains("alpha.gd", StringComparison.OrdinalIgnoreCase) ? "Beta" : "Alpha";
            Assert.AreEqual($"public {peerType} peer;", variableMapping.Result.CSharpSyntax!.ToString());
            Assert.IsNotNull(variableMapping.Parent);
            Assert.IsTrue(variableMapping.MatchingRules.Contains("fallback-ignore"));
        }

        Assert.AreEqual(2, plan.Files.Count);
        Assert.IsTrue(plan.Files.All(file =>
            file.Types.Single().Declaration is TypeDeclarationSyntax declaration && declaration.Members.Count == 2));
        Assert.IsTrue(plan.Files.All(file =>
            file.Types.Single().Declaration is TypeDeclarationSyntax declaration &&
            declaration.Members.OfType<MethodDeclarationSyntax>().Single().Body?.Statements.Count == 1));
    }

    [TestMethod]
    public async Task CreatePlan_ReportsUnmappedSyntaxInsteadOfAssumingCoverage()
    {
        using var analysis = await CreateAnalysisAsync();

        var plan = new GDConversionService().CreatePlan(analysis, GDConversionRuleSet.Empty);

        Assert.IsFalse(plan.IsComplete);
        Assert.IsTrue(plan.UnmappedNodes.Any());
        Assert.IsTrue(plan.UnmappedNodes.All(mapping =>
            mapping.Result.Disposition == GDConversionNodeDisposition.Unmapped));
    }

    [TestMethod]
    public async Task CreatePlan_UsesSourcePathAndDefaultAssemblyWhenDestinationIsOmitted()
    {
        using var analysis = await CreateAnalysisAsync();
        var rules = new GDConversionRuleSet(
        [
            new IgnoreNodeRule(),
            new ImplicitClassMappingRule()
        ]);

        var plan = new GDConversionService().CreatePlan(analysis, rules);

        Assert.IsTrue(plan.IsComplete);
        CollectionAssert.AreEquivalent(
            new[] { "alpha.cs", "beta.cs" },
            plan.Files.Select(file => file.OutputPath).ToArray());
        Assert.IsTrue(plan.Files.All(file => file.AssemblyName == null));
    }

    [TestMethod]
    public async Task CreatePlan_ResolvesDestinationPathAssemblyAndNamespace()
    {
        using var analysis = await CreateAnalysisAsync();
        var rules = new GDConversionRuleSet(
        [
            new IgnoreNodeRule(),
            new ClassMappingRule(
                pathFormat: Path.Combine("utilities", "Extensions.cs"),
                declarationName: "Extensions",
                destinationName: "Project.Utilities.Extensions",
                assemblyName: "Project.Utilities"),
            new VariableMappingRule(),
            new MethodMappingRule(),
            new StatementsMappingRule(),
            new PassMappingRule()
        ]);

        var plan = new GDConversionService().CreatePlan(analysis, rules);

        Assert.AreEqual(1, plan.Files.Count);
        Assert.IsTrue(plan.Files.All(file => file.OutputPath == Path.Combine("utilities", "Extensions.cs")));
        Assert.IsTrue(plan.Files.All(file => file.AssemblyName == "Project.Utilities"));
        Assert.IsTrue(plan.Files.All(file => file.Types.Single().Destination.FullName == "Project.Utilities.Extensions"));
    }

    [TestMethod]
    public async Task WriteOutputs_RoutesExplicitContributionToHoistedInnerClassDestination()
    {
        File.WriteAllText(
            Path.Combine(_projectDirectory, "alpha.gd"),
            "class_name Alpha\nextends RefCounted\nclass Inner:\n\tpass\n");
        using var analysis = await CreateAnalysisAsync();
        var rules = new GDConversionRuleSet(
        [
            new IgnoreNodeRule(),
            new ClassMappingRule(),
            new InnerClassMappingRule(),
            new RedirectedMethodRule(),
            new StatementsMappingRule(),
            new PassMappingRule()
        ]);
        var service = new GDConversionService();
        var plan = service.CreatePlan(analysis, rules);
        var outputDirectory = Path.Combine(_projectDirectory, "generated");

        service.WriteOutputs(plan, outputDirectory);

        var alphaContent = File.ReadAllText(Path.Combine(outputDirectory, "alpha.cs"));
        StringAssert.Contains(alphaContent, "class Inner");
        StringAssert.Contains(alphaContent, "PingBeta");
    }

    [TestMethod]
    public async Task WriteOutputs_EmitsPromotedInnerClassAsSeparateTopLevelFile()
    {
        File.WriteAllText(
            Path.Combine(_projectDirectory, "alpha.gd"),
            "class_name Alpha\nextends RefCounted\nclass Inner:\n\tpass\n");
        using var analysis = await CreateAnalysisAsync();
        var rules = new GDConversionRuleSet(
        [
            new IgnoreNodeRule(),
            new ClassMappingRule(),
            new HoistedInnerClassMappingRule(),
            new VariableMappingRule(),
            new MethodMappingRule(),
            new StatementsMappingRule(),
            new PassMappingRule()
        ]);
        var service = new GDConversionService();
        var plan = service.CreatePlan(analysis, rules);
        var outputDirectory = Path.Combine(_projectDirectory, "generated");

        service.WriteOutputs(plan, outputDirectory);

        var alphaContent = File.ReadAllText(Path.Combine(outputDirectory, "alpha.cs"));
        var innerContent = File.ReadAllText(Path.Combine(outputDirectory, "inner", "Inner.cs"));
        Assert.IsFalse(alphaContent.Contains("class Inner", StringComparison.Ordinal));
        StringAssert.Contains(innerContent, "public class Inner");
    }

    [TestMethod]
    public async Task WriteOutputs_WritesMappedDeclarationWithComposedMembers()
    {
        using var analysis = await CreateAnalysisAsync();
        var rules = new GDConversionRuleSet(
        [
            new IgnoreNodeRule(),
            new ClassMappingRule(),
            new VariableMappingRule(),
            new MethodMappingRule(),
            new StatementsMappingRule(),
            new PassMappingRule()
        ]);
        var service = new GDConversionService();
        var plan = service.CreatePlan(analysis, rules);
        var outputDirectory = Path.Combine(_projectDirectory, "generated");

        service.WriteOutputs(plan, outputDirectory);

        foreach (var file in plan.Files)
        {
            var content = File.ReadAllText(Path.Combine(outputDirectory, file.OutputPath));
            var declaration = file.Types.Single().Declaration;
            StringAssert.Contains(content, $"class {declaration.Identifier.ValueText}");
            var peerType = declaration.Identifier.ValueText == "Alpha" ? "Beta" : "Alpha";
            StringAssert.Contains(content, $"public {peerType} peer;");
            StringAssert.Contains(content, "public void Ping()");
        }
    }

    [TestMethod]
    public async Task WriteOutputs_RejectsIncompletePlanBeforeWriting()
    {
        using var analysis = await CreateAnalysisAsync();
        var plan = new GDConversionService().CreatePlan(analysis, GDConversionRuleSet.Empty);
        var outputDirectory = Path.Combine(_projectDirectory, "generated");

        Assert.ThrowsException<InvalidOperationException>(() =>
            new GDConversionService().WriteOutputs(plan, outputDirectory));
        Assert.IsFalse(Directory.Exists(outputDirectory));
    }

    [TestMethod]
    public async Task CreatePlan_MergesContributionsForEquivalentOutputPathsInConfiguredOrder()
    {
        using var analysis = await CreateAnalysisAsync();
        var rules = new GDConversionRuleSet(
        [
            new IgnoreNodeRule(),
            new ClassMappingRule(
                declarationName: "UtilitiesExtensions",
                betaFirst: true,
                betaPathFormat: @".\Extensions.cs",
                pathFormat: "Extensions.cs"),
            new VariableMappingRule(),
            new MethodMappingRule("UtilitiesExtensions", includeClassName: true, order: -10)
        ]);

        var service = new GDConversionService();
        var plan = service.CreatePlan(analysis, rules);

        Assert.IsTrue(plan.IsComplete);
        var file = plan.Files.Single();
        Assert.AreEqual("Extensions.cs", file.OutputPath);
        var type = file.Types.Single();
        Assert.AreEqual("UtilitiesExtensions", type.Declaration.Identifier.ValueText);
        Assert.AreEqual(4, type.Contributions.Count);
        var memberNames = ((TypeDeclarationSyntax)type.Declaration).Members
            .Select(member => member switch
            {
                MethodDeclarationSyntax method => method.Identifier.ValueText,
                FieldDeclarationSyntax field => field.Declaration.Variables.Single().Identifier.ValueText,
                _ => member.Kind().ToString()
            });
        Assert.AreEqual("PingAlpha|PingBeta|peer|peer", string.Join("|", memberNames));

        var outputDirectory = Path.Combine(_projectDirectory, "shared-output");
        service.WriteOutputs(plan, outputDirectory);
        var content = File.ReadAllText(Path.Combine(outputDirectory, "Extensions.cs"));
        StringAssert.Contains(content, "PingBeta");
        StringAssert.Contains(content, "PingAlpha");
        Assert.AreEqual(1, Directory.GetFiles(outputDirectory, "*.cs").Length);
    }

    [TestMethod]
    public async Task CreatePlan_RejectsIncompatibleDeclarationsForSameOutputPath()
    {
        using var analysis = await CreateAnalysisAsync();
        var rules = new GDConversionRuleSet(
        [
            new IgnoreNodeRule(),
            new ConflictingClassMappingRule()
        ]);

        Assert.ThrowsException<InvalidOperationException>(() =>
            new GDConversionService().CreatePlan(analysis, rules));
    }

    [TestMethod]
    public async Task WriteOutputs_RejectsPathsOutsideOutputDirectoryBeforeWritingAnyFiles()
    {
        using var analysis = await CreateAnalysisAsync();
        var rules = new GDConversionRuleSet(
        [
            new IgnoreNodeRule(),
            new ClassMappingRule("../{type}.cs"),
            new VariableMappingRule(),
            new MethodMappingRule(),
            new StatementsMappingRule(),
            new PassMappingRule()
        ]);
        var service = new GDConversionService();
        var plan = service.CreatePlan(analysis, rules);
        var outputDirectory = Path.Combine(_projectDirectory, "generated");

        Assert.ThrowsException<InvalidOperationException>(() => service.WriteOutputs(plan, outputDirectory));
        Assert.IsFalse(Directory.Exists(outputDirectory));
    }

    [TestMethod]
    public async Task WriteOutputs_AllowsAddonOutputAndSiblingClassLibraryWithinSolution()
    {
        var projectRoot = CreateSolutionProject(out var solutionRoot);
        var outputDirectory = Path.Combine(projectRoot, "addons", "guideCS");
        var classLibraryOutput = Path.Combine(solutionRoot, "src", "Features", "Guide", "GuideGenerated.cs");
        var relativeLibraryPath = Path.GetRelativePath(outputDirectory, classLibraryOutput);
        using var analysis = await GDConversionAnalyzer.AnalyzeAsync(
            projectRoot,
            new GDConversionAnalysisOptions { MaxDegreeOfParallelism = 1, EnrichCallSites = false });
        var rules = new GDConversionRuleSet(
        [
            new IgnoreNodeRule(),
            new ClassMappingRule(betaPathFormat: relativeLibraryPath),
            new VariableMappingRule(),
            new MethodMappingRule(),
            new StatementsMappingRule(),
            new PassMappingRule()
        ]);
        var service = new GDConversionService();
        var plan = service.CreatePlan(analysis, rules);

        service.WriteOutputs(plan, Path.Combine("addons", "guideCS"));

        Assert.IsTrue(File.Exists(Path.Combine(outputDirectory, "alpha.cs")));
        Assert.IsTrue(File.Exists(classLibraryOutput));
        Assert.IsFalse(Directory.Exists(Path.Combine(projectRoot, "addons", "other")));
    }

    [TestMethod]
    public async Task WriteOutputs_RejectsOtherFoldersInsideGodotProject()
    {
        var projectRoot = CreateSolutionProject(out _);
        var outputDirectory = Path.Combine(projectRoot, "addons", "guideCS");
        var otherProjectOutput = Path.Combine(projectRoot, "addons", "other", "Generated.cs");
        var relativePath = Path.GetRelativePath(outputDirectory, otherProjectOutput);
        using var analysis = await GDConversionAnalyzer.AnalyzeAsync(
            projectRoot,
            new GDConversionAnalysisOptions { MaxDegreeOfParallelism = 1, EnrichCallSites = false });
        var plan = new GDConversionService().CreatePlan(analysis, CreateCompleteRules(relativePath));

        Assert.ThrowsException<InvalidOperationException>(() =>
            new GDConversionService().WriteOutputs(plan, Path.Combine("addons", "guideCS")));
        Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(otherProjectOutput)));
    }

    [TestMethod]
    public async Task WriteOutputs_RejectsPathsOutsideSolutionRoot()
    {
        var projectRoot = CreateSolutionProject(out _);
        var outputDirectory = Path.Combine(projectRoot, "addons", "guideCS");
        var outsideSolutionOutput = Path.Combine(_projectDirectory, "outside", "Generated.cs");
        var relativePath = Path.GetRelativePath(outputDirectory, outsideSolutionOutput);
        using var analysis = await GDConversionAnalyzer.AnalyzeAsync(
            projectRoot,
            new GDConversionAnalysisOptions { MaxDegreeOfParallelism = 1, EnrichCallSites = false });
        var plan = new GDConversionService().CreatePlan(analysis, CreateCompleteRules(relativePath));

        Assert.ThrowsException<InvalidOperationException>(() =>
            new GDConversionService().WriteOutputs(plan, Path.Combine("addons", "guideCS")));
        Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(outsideSolutionOutput)));
    }

    private Task<GDConversionAnalysis> CreateAnalysisAsync()
    {
        return GDConversionAnalyzer.AnalyzeAsync(
            _projectDirectory,
            new GDConversionAnalysisOptions { MaxDegreeOfParallelism = 1, EnrichCallSites = false });
    }

    private string CreateSolutionProject(out string solutionRoot)
    {
        solutionRoot = Path.Combine(_projectDirectory, "solution");
        var projectRoot = Path.Combine(solutionRoot, "game", "client");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(solutionRoot, "MyProject.sln"), string.Empty);
        File.WriteAllText(Path.Combine(projectRoot, "project.godot"), "config_version=5\n");
        File.WriteAllText(Path.Combine(projectRoot, "alpha.gd"), "class_name Alpha\nextends RefCounted\nvar peer: Beta\nfunc ping():\n\tpass\n");
        File.WriteAllText(Path.Combine(projectRoot, "beta.gd"), "class_name Beta\nextends RefCounted\nvar peer: Alpha\nfunc ping():\n\tpass\n");
        return projectRoot;
    }

    private static GDConversionRuleSet CreateCompleteRules(string pathFormat)
        => new(
        [
            new IgnoreNodeRule(),
            new ClassMappingRule(pathFormat),
            new VariableMappingRule(),
            new MethodMappingRule(),
            new StatementsMappingRule(),
            new PassMappingRule()
        ]);

    private sealed class IgnoreNodeRule : IGDConversionRule
    {
        public string Name => "fallback-ignore";
        public int Priority => 0;

        public bool CanConvert(GDConversionNodeContext context) => true;

        public GDConversionNodeResult Convert(GDConversionNodeContext context)
        {
            Assert.IsNotNull(context.SemanticModel);
            if (context.Node is GDVariableDeclaration)
            {
                Assert.IsNotNull(context.Parent);
                Assert.AreEqual(context.Node, context.Parent.Nodes.First(child => ReferenceEquals(child, context.Node)));
            }

            return GDConversionNodeResult.Ignored($"No C# syntax is needed for {context.Syntax.TypeName}.");
        }
    }

    private sealed class VariableMappingRule : IGDConversionRule
    {
        public string Name => "variable-specialist";
        public int Priority => 10;

        public bool CanConvert(GDConversionNodeContext context) => context.Node is GDVariableDeclaration;

        public GDConversionNodeResult Convert(GDConversionNodeContext context)
        {
            var peerType = context.Script.TypeName == "Alpha" ? "Beta" : "Alpha";
            return GDConversionNodeResult.Converted(SyntaxFactory.ParseMemberDeclaration($"public {peerType} peer;")!);
        }
    }

    private sealed class ClassMappingRule(
        string? pathFormat = null,
        string? declarationName = null,
        bool betaFirst = false,
        string? betaPathFormat = null,
        string? assemblyName = null,
        string? innerClassName = null,
        string? destinationName = null) : IGDConversionRule
    {
        public string Name => "class-mapping";
        public int Priority => 10;

        public bool CanConvert(GDConversionNodeContext context) => context.Node is GDClassDeclaration;

        public GDConversionNodeResult Convert(GDConversionNodeContext context)
        {
            var typeName = context.Script.TypeName ?? "Script";
            var relativePath = context.Script.TypeName == "Beta" && betaPathFormat != null
                ? betaPathFormat
                : pathFormat;
            var destination = new GDConversionDestination(
                destinationName ?? declarationName ?? typeName,
                assemblyName,
                relativePath?.Replace("{type}", typeName),
                innerClassName,
                betaFirst ? context.Script.TypeName == "Alpha" ? 100 : 10 : 0);
            return GDConversionNodeResult.Converted(
                SyntaxFactory.ClassDeclaration(declarationName ?? typeName),
                destination);
        }

    }

    private sealed class ImplicitClassMappingRule : IGDConversionRule
    {
        public string Name => "implicit-class-mapping";
        public int Priority => 10;

        public bool CanConvert(GDConversionNodeContext context) => context.Node is GDClassDeclaration;

        public GDConversionNodeResult Convert(GDConversionNodeContext context)
            => GDConversionNodeResult.Converted(
                SyntaxFactory.ClassDeclaration(context.Script.TypeName ?? "Script"));
    }

    private sealed class InnerClassMappingRule : IGDConversionRule
    {
        public string Name => "inner-class-mapping";
        public int Priority => 10;

        public bool CanConvert(GDConversionNodeContext context) => context.Node is GDInnerClassDeclaration;

        public GDConversionNodeResult Convert(GDConversionNodeContext context)
        {
            var innerClass = (GDInnerClassDeclaration)context.Node!;
            return GDConversionNodeResult.Converted(SyntaxFactory.ClassDeclaration(innerClass.Identifier.Sequence));
        }
    }

    private sealed class HoistedInnerClassMappingRule : IGDConversionRule
    {
        public string Name => "hoisted-inner-class";
        public int Priority => 10;

        public bool CanConvert(GDConversionNodeContext context) => context.Node is GDInnerClassDeclaration;

        public GDConversionNodeResult Convert(GDConversionNodeContext context)
        {
            var innerClass = (GDInnerClassDeclaration)context.Node!;
            return GDConversionNodeResult.Converted(
                SyntaxFactory.ClassDeclaration(innerClass.Identifier.Sequence).AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword)),
                new GDConversionDestination(
                    $"Alpha.{innerClass.Identifier.Sequence}",
                    RelativePath: Path.Combine("inner", $"{innerClass.Identifier.Sequence}.cs")));
        }
    }

    private sealed class RedirectedMethodRule : IGDConversionRule
    {
        public string Name => "redirected-method";
        public int Priority => 10;

        public bool CanConvert(GDConversionNodeContext context) => context.Node is GDMethodDeclaration;

        public GDConversionNodeResult Convert(GDConversionNodeContext context)
        {
            var methodName = $"Ping{context.Script.TypeName}";
            var destination = context.Script.TypeName == "Beta"
                ? new GDConversionDestination("Alpha", InnerClassName: "Inner")
                : null;
            return GDConversionNodeResult.Converted(
                SyntaxFactory.ParseMemberDeclaration($"public void {methodName}();")!,
                destination);
        }
    }

    private sealed class ConflictingClassMappingRule : IGDConversionRule
    {
        public string Name => "conflicting-class-mapping";
        public int Priority => 10;

        public bool CanConvert(GDConversionNodeContext context) => context.Node is GDClassDeclaration;

        public GDConversionNodeResult Convert(GDConversionNodeContext context)
            => GDConversionNodeResult.Converted(
                SyntaxFactory.ClassDeclaration(context.Script.TypeName ?? "Script"),
                new GDConversionDestination("SharedType", RelativePath: "Extensions.cs"));
    }

    private sealed class MethodMappingRule(
        string? destinationName = null,
        bool includeClassName = false,
        int order = 0) : IGDConversionRule
    {
        public string Name => "method-mapping";
        public int Priority => 10;

        public bool CanConvert(GDConversionNodeContext context) => context.Node is GDMethodDeclaration;

        public GDConversionNodeResult Convert(GDConversionNodeContext context)
        {
            var methodName = includeClassName ? $"Ping{context.Script.TypeName}" : "Ping";
            return GDConversionNodeResult.Converted(
                SyntaxFactory.ParseMemberDeclaration($"public void {methodName}();")!,
                destinationName == null
                    ? null
                    : new GDConversionDestination(destinationName, Order: order));
        }
    }

    private sealed class StatementsMappingRule : IGDConversionRule
    {
        public string Name => "statements-mapping";
        public int Priority => 10;

        public bool CanConvert(GDConversionNodeContext context) => context.Node is GDStatementsList;

        public GDConversionNodeResult Convert(GDConversionNodeContext context)
            => GDConversionNodeResult.Converted(SyntaxFactory.Block());
    }

    private sealed class PassMappingRule : IGDConversionRule
    {
        public string Name => "pass-mapping";
        public int Priority => 10;

        public bool CanConvert(GDConversionNodeContext context)
            => context.Node is GDExpressionStatement { Expression: GDPassExpression };

        public GDConversionNodeResult Convert(GDConversionNodeContext context)
            => GDConversionNodeResult.Converted(SyntaxFactory.EmptyStatement());
    }
}
