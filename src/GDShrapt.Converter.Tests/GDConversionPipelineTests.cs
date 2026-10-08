using GDShrapt.Converter.Planning;
using GDShrapt.Converter.Planning.Definitions;
using GDShrapt.Abstractions;
using GDShrapt.Reader;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics;

namespace GDShrapt.Converter.Tests;

[TestClass]
public sealed class GDConversionPipelineTests
{
    private string _testRootDirectory = null!;
    private string _projectDirectory = null!;
    private string _testGuid = null!;
    private GDSolutionContext _solutionContext = null!;

    [TestInitialize]
    public void Initialize()
    {
        // Create the test environment from the local `_repo` folder.
        _testGuid = Guid.NewGuid().ToString("N");
        var repoPath = Path.Combine(AppContext.BaseDirectory, "_repo");
        _testRootDirectory = Path.Combine(Path.GetTempPath(), "GDShrapt.Converter.Tests", _testGuid);
        Directory.CreateDirectory(_testRootDirectory);

        CopyDirectory(repoPath, _testRootDirectory);
        ReplaceInFile(Path.Combine(_testRootDirectory, ".editorconfig"), "{test-guid}", _testGuid);

        _projectDirectory = Path.Combine(_testRootDirectory, "game", "client");
        // Create supplemental projects.

        _solutionContext = new GDSolutionContext(
            Path.Combine(_projectDirectory, "GDShrapt.Tests.csproj"),
            "DebugTest");
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

        var service = new GDConversionService(_solutionContext);
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
    public async Task PrivateFieldRule_MapsUnderscoreFieldToTypedPrivateField()
    {
        File.WriteAllText(
            Path.Combine(_projectDirectory, "alpha.gd"),
            "class_name Alpha\nextends RefCounted\nvar _health: int = 5\n");
        using var analysis = await CreateAnalysisAsync();
        var plan = new GDConversionService(_solutionContext).CreatePlan(
            analysis,
            new GDConversionRuleSet([new PrivateFieldRule(), new NumberMappingRule(), new IgnoreNodeRule()]));

        var mapping = plan.Entries
            .SelectMany(entry => entry.Mappings)
            .Single(candidate => candidate.Node is GDVariableDeclaration { Identifier.Sequence: "_health" });

        Assert.AreEqual("private-field", mapping.RuleName);
        Assert.AreEqual("private readonly long _health = 5;", mapping.Result.CSharpSyntax!.ToString());
    }

    [TestMethod]
    public async Task PrivateFieldRule_DoesNotMarkFieldReadonlyWhenItIsReassigned()
    {
        File.WriteAllText(
            Path.Combine(_projectDirectory, "alpha.gd"),
            "class_name Alpha\nextends RefCounted\nvar _health: int = 5\nfunc update():\n\t_health = 7\n");
        using var analysis = await CreateAnalysisAsync();
        var plan = new GDConversionService(_solutionContext).CreatePlan(
            analysis,
            new GDConversionRuleSet([new PrivateFieldRule(), new NumberMappingRule(), new IgnoreNodeRule()]));

        var mapping = plan.Entries
            .SelectMany(entry => entry.Mappings)
            .Single(candidate => candidate.Node is GDVariableDeclaration { Identifier.Sequence: "_health" });

        Assert.AreEqual("private long _health = 5;", mapping.Result.CSharpSyntax!.ToString());
    }

    [TestMethod]
    public async Task PrivateFieldRule_DoesNotMapAccessorBackedProperties()
    {
        File.WriteAllText(
            Path.Combine(_projectDirectory, "alpha.gd"),
            "class_name Alpha\nextends RefCounted\nvar _health: int:\n\tget:\n\t\treturn 1\n");
        using var analysis = await CreateAnalysisAsync();
        var plan = new GDConversionService(_solutionContext).CreatePlan(
            analysis,
            new GDConversionRuleSet([new PrivateFieldRule(), new IgnoreNodeRule()]));

        var mapping = plan.Entries
            .SelectMany(entry => entry.Mappings)
            .Single(candidate => candidate.Node is GDVariableDeclaration { Identifier.Sequence: "_health" });

        Assert.AreEqual("fallback-ignore", mapping.RuleName);
    }

    [TestMethod]
    public async Task PrivateFieldRule_UsesSelectedProjectsRealPrecision()
    {
        File.WriteAllText(
            Path.Combine(_projectDirectory, "alpha.gd"),
            "class_name Alpha\nextends RefCounted\nvar _speed: float = 1\n");
        using var analysis = await CreateAnalysisAsync();
        var rules = new GDConversionRuleSet([new PrivateFieldRule(), new IgnoreNodeRule()]);

        var singlePrecisionPlan = new GDConversionService(TestSolutionContext.WithDoublePrecision(false))
            .CreatePlan(analysis, rules);
        var doublePrecisionPlan = new GDConversionService(TestSolutionContext.WithDoublePrecision(true))
            .CreatePlan(analysis, rules);
        var singlePrecisionField = singlePrecisionPlan.Entries
            .SelectMany(entry => entry.Mappings)
            .Single(mapping => mapping.Node is GDVariableDeclaration { Identifier.Sequence: "_speed" });
        var doublePrecisionField = doublePrecisionPlan.Entries
            .SelectMany(entry => entry.Mappings)
            .Single(mapping => mapping.Node is GDVariableDeclaration { Identifier.Sequence: "_speed" });

        Assert.AreEqual(
            "private readonly float _speed = 1.0f;",
            singlePrecisionField.Result.CSharpSyntax!.NormalizeWhitespace().ToFullString());
        Assert.AreEqual(
            "private readonly double _speed = 1.0;",
            doublePrecisionField.Result.CSharpSyntax!.NormalizeWhitespace().ToFullString());
    }

    [TestMethod]
    public async Task PrivateFieldRule_TranslatesGodotConstructorInitializers()
    {
        File.WriteAllText(
            Path.Combine(_projectDirectory, "alpha.gd"),
            "class_name Alpha\nextends RefCounted\nvar _position: Vector2 = Vector2(1, 2)\n");
        using var analysis = await CreateAnalysisAsync();
        var plan = new GDConversionService(_solutionContext).CreatePlan(
            analysis,
            new GDConversionRuleSet([new ClassMappingRule(), new PrivateFieldRule(), new IgnoreNodeRule()]));

        var field = plan.Files
            .SelectMany(file => file.Types)
            .Single(plannedType => plannedType.Declaration.Identifier.ValueText == "Alpha")
            .Declaration is TypeDeclarationSyntax type
            ? type.Members.OfType<FieldDeclarationSyntax>().Single()
            : throw new AssertFailedException("Expected a generated type declaration.");

        Assert.AreEqual(
            "private readonly Godot.Vector2 _position = new Godot.Vector2(1.0f, 2.0f);",
            field.NormalizeWhitespace().ToFullString());
    }

    [TestMethod]
    public async Task PrivateFieldRule_UsesPolicySelectedNativeRepresentationAtUseSite()
    {
        File.WriteAllText(
            Path.Combine(_projectDirectory, "alpha.gd"),
            "class_name Alpha\nextends RefCounted\nvar _position: Vector2 = Vector2(1, 2)\n");
        using var analysis = await CreateAnalysisAsync();
        var plan = new GDConversionService(_solutionContext, new NativeVector2Policy()).CreatePlan(
            analysis,
            new GDConversionRuleSet([new PrivateFieldRule(), new IgnoreNodeRule()]));
        var field = plan.Entries
            .SelectMany(entry => entry.Mappings)
            .Single(mapping => mapping.Node is GDVariableDeclaration { Identifier.Sequence: "_position" })
            .Result.CSharpSyntax!;

        Assert.AreEqual(
            "private readonly System.Numerics.Vector2 _position = new System.Numerics.Vector2(1.0f, 2.0f);",
            field.NormalizeWhitespace().ToFullString());
    }

    [TestMethod]
    public async Task WriteOutputs_AdoptsNamespaceUsingContributionAndShortensTypeNames()
    {
        File.WriteAllText(
            Path.Combine(_projectDirectory, "alpha.gd"),
            "class_name Alpha\nextends RefCounted\nvar _position: Vector2 = Vector2(1, 2)\nvar _positions: Array[Vector2] = [Vector2(1, 2)]\n");
        using var analysis = await CreateAnalysisAsync();
        var service = new GDConversionService(_solutionContext);
        var plan = service.CreatePlan(
            analysis,
            new GDConversionRuleSet([new ClassMappingRule(), new PrivateFieldRule(), new IgnoreNodeRule()]));
        Assert.IsTrue(plan.Files.SelectMany(file => file.Types)
            .SelectMany(type => type.Contributions)
            .SelectMany(mapping => mapping.Result.Contributions)
            .OfType<GDConversionNamespaceUsingContribution>()
            .Any(contribution => contribution.Namespace == "Godot"));
        var outputDirectory = Path.Combine(_projectDirectory, "generated");

        service.WriteOutputs(plan, outputDirectory);

        var output = File.ReadAllText(Path.Combine(outputDirectory, "alpha.cs"));
        StringAssert.Contains(output, "using Godot;");
        StringAssert.Contains(output, "using Godot.Collections;");
        StringAssert.Contains(output, "Vector2 _position = new Vector2(1.0f, 2.0f)");
        StringAssert.Contains(output, "Array<Vector2> _positions = new Array<Vector2>");
        Assert.IsFalse(output.Contains("Godot.Vector2", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task PrivateFieldRule_TranslatesGodotNewFactoryCalls()
    {
        File.WriteAllText(
            Path.Combine(_projectDirectory, "alpha.gd"),
            "class_name Alpha\nextends RefCounted\nvar _resource: RefCounted = RefCounted.new()\n");
        using var analysis = await CreateAnalysisAsync();
        var plan = new GDConversionService(_solutionContext).CreatePlan(
            analysis,
            new GDConversionRuleSet([new PrivateFieldRule(), new IgnoreNodeRule()]));

        var field = plan.Entries
            .SelectMany(entry => entry.Mappings)
            .Single(mapping => mapping.Node is GDVariableDeclaration { Identifier.Sequence: "_resource" })
            .Result.CSharpSyntax!;

        Assert.AreEqual(
            "private readonly Godot.RefCounted _resource = new Godot.RefCounted();",
            field.NormalizeWhitespace().ToFullString());
    }

    [TestMethod]
    public async Task PrivateFieldRule_ContributesInitParameterAssignmentsToGeneratedConstructor()
    {
        File.WriteAllText(
            Path.Combine(_projectDirectory, "alpha.gd"),
            "class_name Alpha\nextends RefCounted\nvar _health: int\nfunc _init(health: int):\n\t_health = health\n");
        using var analysis = await CreateAnalysisAsync();
        var plan = new GDConversionService(_solutionContext).CreatePlan(
            analysis,
            new GDConversionRuleSet([new ClassMappingRule(), new PrivateFieldRule(), new IgnoreNodeRule()]));

        var declaration = (TypeDeclarationSyntax)plan.Files
            .SelectMany(file => file.Types)
            .Single(plannedType => plannedType.Declaration.Identifier.ValueText == "Alpha")
            .Declaration;
        var constructor = declaration.Members.OfType<ConstructorDeclarationSyntax>().Single();

        Assert.AreEqual(
            "public Alpha(long health)\n{\n    this._health = health;\n}",
            constructor.NormalizeWhitespace(eol: "\n").ToFullString());
    }

    [TestMethod]
    public async Task GDSolutionContext_LoadsConfigurationPropertiesAndFormattingConventions()
    {
        var projectFilePath = Path.Combine(_projectDirectory, "GodotGame.csproj");
        File.WriteAllText(
            projectFilePath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>GodotGame</AssemblyName>
                <RootNamespace>Example.Game</RootNamespace>
                <Configurations>Debug;Release</Configurations>
                <DefineConstants Condition="'$(Configuration)' == 'Release'">$(DefineConstants);GODOT_REAL_T_IS_DOUBLE</DefineConstants>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(_projectDirectory, ".editorconfig"),
            """
            root = true

            [*.cs]
            indent_style = tab
            indent_size = 2
            end_of_line = lf
            csharp_style_namespace_declarations = file_scoped
            dotnet_naming_rule.private_fields_rule.symbols = private_fields
            dotnet_naming_rule.private_fields_rule.style = private_style
            dotnet_naming_symbols.private_fields.applicable_kinds = field
            dotnet_naming_symbols.private_fields.applicable_accessibilities = private
            dotnet_naming_style.private_style.capitalization = camel_case
            dotnet_naming_style.private_style.required_prefix = _
            dotnet_naming_rule.static_private_fields_rule.symbols = static_private_fields
            dotnet_naming_rule.static_private_fields_rule.style = static_private_style
            dotnet_naming_symbols.static_private_fields.applicable_kinds = field
            dotnet_naming_symbols.static_private_fields.applicable_accessibilities = private
            dotnet_naming_symbols.static_private_fields.required_modifiers = static
            dotnet_naming_style.static_private_style.capitalization = pascal_case
            dotnet_naming_style.static_private_style.required_prefix = m_
            """);
        File.WriteAllText(Path.Combine(_projectDirectory, ".gitattributes"), "*.cs eol=crlf\n*.gd eol=lf\n");
        File.WriteAllText(Path.Combine(_projectDirectory, "GlobalUsings.cs"), "global using Godot;\nglobal using StringNameAlias = Godot.StringName;\n");

        var solution = new GDSolutionContext(projectFilePath, "Release");

        Assert.AreEqual("GodotGame", solution.DefaultProject.AssemblyName);
        Assert.AreEqual("Example.Game", solution.DefaultProject.RootNamespace);
        Assert.AreEqual("Release", solution.DefaultProject.Configuration);
        Assert.IsTrue(solution.DefaultProject.SupportsDoublePrecision);
        Assert.IsTrue(solution.DefaultProject.GlobalUsings.Contains("Godot"));
        Assert.AreEqual("Godot.StringName", solution.DefaultProject.GlobalTypeAliases["StringNameAlias"]);
        Assert.IsTrue(solution.DefaultFormatter.Options.UseTabIndent);
        Assert.AreEqual(2, solution.DefaultFormatter.Options.IndentSize);
        Assert.AreEqual("\n", solution.DefaultFormatter.Options.LineEnding);
        Assert.IsTrue(solution.DefaultFormatter.Options.PreferFileScopedNamespaces);
        Assert.AreEqual("_privateField", solution.DefaultFormatter.Name("private_field"));
        Assert.AreEqual(
            "m_PrivateField",
            solution.DefaultFormatter.Name(
                "private_field",
                new GDConversionNamingContext(
                    "field",
                    "private",
                    new HashSet<string>(["static"], StringComparer.OrdinalIgnoreCase))));
        Assert.AreEqual(
            "\r\n",
            solution.DefaultFormatter.Options.GetLineEnding(Path.Combine(_projectDirectory, "Generated.cs")));
        Assert.AreEqual(
            "\n",
            solution.DefaultFormatter.Options.GetLineEnding(Path.Combine(_projectDirectory, "Generated.gd")));

        File.Delete(Path.Combine(_projectDirectory, "beta.gd"));
        using var analysis = await CreateAnalysisAsync();
        var conversion = new GDConversionService(solution);
        var plan = conversion.CreatePlan(
            analysis,
            new GDConversionRuleSet(
            [
                new IgnoreNodeRule(),
                new ClassMappingRule(
                    pathFormat: "Generated.cs",
                    declarationName: "Alpha",
                    destinationName: "Example.Game.Alpha"),
                new MethodMappingRule()
            ]));
        var outputDirectory = Path.Combine(_projectDirectory, "generated");
        conversion.WriteOutputs(plan, outputDirectory);
        var output = File.ReadAllText(Path.Combine(outputDirectory, "Generated.cs"));

        StringAssert.Contains(output, "namespace Example.Game;");
        StringAssert.Contains(output, "\tpublic void Ping();");
        Assert.IsTrue(output.Contains("\r\n"));
    }

    [TestMethod]
    public async Task GDSolutionContext_UsesGlobalAliasesForGodotTypes()
    {
        var projectFilePath = Path.Combine(_projectDirectory, "GodotGame.csproj");
        File.WriteAllText(
            projectFilePath,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(
            Path.Combine(_projectDirectory, "GlobalUsings.cs"),
            "global using Godot;\nglobal using Name = Godot.StringName;\n");
        File.WriteAllText(
            Path.Combine(_projectDirectory, "alpha.gd"),
            "class_name Alpha\nextends RefCounted\nvar _name: StringName = &\"hero\"\n");
        using var analysis = await CreateAnalysisAsync();
        var plan = new GDConversionService(new GDSolutionContext(projectFilePath)).CreatePlan(
            analysis,
            new GDConversionRuleSet([new PrivateFieldRule(), new IgnoreNodeRule()]));
        var field = plan.Entries
            .SelectMany(entry => entry.Mappings)
            .Single(mapping => mapping.Node is GDVariableDeclaration { Identifier.Sequence: "_name" })
            .Result.CSharpSyntax!;

        Assert.AreEqual("private readonly Name _name = new Name(\"hero\");", field.NormalizeWhitespace().ToFullString());
    }

    [TestMethod]
    public async Task CreatePlan_ReportsUnmappedSyntaxInsteadOfAssumingCoverage()
    {
        using var analysis = await CreateAnalysisAsync();

        var plan = new GDConversionService(_solutionContext).CreatePlan(analysis, GDConversionRuleSet.Empty);

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

        var plan = new GDConversionService(_solutionContext).CreatePlan(analysis, rules);

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

        var plan = new GDConversionService(_solutionContext).CreatePlan(analysis, rules);

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
        var service = new GDConversionService(_solutionContext);
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
        var service = new GDConversionService(_solutionContext);
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
        var service = new GDConversionService(_solutionContext);
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
        var plan = new GDConversionService(_solutionContext).CreatePlan(analysis, GDConversionRuleSet.Empty);
        var outputDirectory = Path.Combine(_projectDirectory, "generated");

        Assert.ThrowsException<InvalidOperationException>(() =>
            new GDConversionService(_solutionContext).WriteOutputs(plan, outputDirectory));
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

        var service = new GDConversionService(_solutionContext);
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
            new GDConversionService(_solutionContext).CreatePlan(analysis, rules));
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
        var service = new GDConversionService(_solutionContext);
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
        var service = new GDConversionService(_solutionContext);
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
        var plan = new GDConversionService(_solutionContext).CreatePlan(analysis, CreateCompleteRules(relativePath));

        Assert.ThrowsException<InvalidOperationException>(() =>
            new GDConversionService(_solutionContext).WriteOutputs(plan, Path.Combine("addons", "guideCS")));
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
        var plan = new GDConversionService(_solutionContext).CreatePlan(analysis, CreateCompleteRules(relativePath));

        Assert.ThrowsException<InvalidOperationException>(() =>
            new GDConversionService(_solutionContext).WriteOutputs(plan, Path.Combine("addons", "guideCS")));
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

    private sealed class NativeVector2Policy : IGDConversionTypeRepresentationPolicy
    {
        public GDNativeTypeRepresentation? GetNativeTypeName(
            string godotTypeName,
            GDSemanticType? semanticType,
            GDNode useSite,
            GDConversionNodeContext context)
            => godotTypeName == "Vector2" &&
               context.Node is GDVariableDeclaration { Identifier.Sequence: "_position" }
                ? new GDNativeTypeRepresentation("System.Numerics.Vector2", GDConversionRealPrecision.Single)
                : null;
    }

    private sealed class IgnoreNodeRule : IGDConversionRule
    {
        public string Name => "fallback-ignore";
        public int Priority => 0;
        public List<IGDConversionRuleMetadata> Metadata { get; set; } = [];

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
        public List<IGDConversionRuleMetadata> Metadata { get; set; } = [];

        public bool CanConvert(GDConversionNodeContext context) => context.Node is GDVariableDeclaration;

        public GDConversionNodeResult Convert(GDConversionNodeContext context)
        {
            var peerType = context.Script.TypeName == "Alpha" ? "Beta" : "Alpha";
            return GDConversionNodeResult.Converted(SyntaxFactory.ParseMemberDeclaration($"public {peerType} peer;")!);
        }

    }

    private sealed class NumberMappingRule : IGDConversionRule
    {
        public string Name => "number-mapping";
        public int Priority => 10;
        public List<IGDConversionRuleMetadata> Metadata { get; set; } = [];

        public bool CanConvert(GDConversionNodeContext context) => context.Node is GDNumberExpression;

        public GDConversionNodeResult Convert(GDConversionNodeContext context)
            => GDConversionNodeResult.Converted(
                SyntaxFactory.ParseExpression(((GDNumberExpression)context.Node!).Number.Sequence));
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
        public List<IGDConversionRuleMetadata> Metadata { get; set; } = [];

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
        public List<IGDConversionRuleMetadata> Metadata { get; set; } = [];

        public bool CanConvert(GDConversionNodeContext context) => context.Node is GDClassDeclaration;

        public GDConversionNodeResult Convert(GDConversionNodeContext context)
            => GDConversionNodeResult.Converted(
                SyntaxFactory.ClassDeclaration(context.Script.TypeName ?? "Script"));
    }

    private sealed class InnerClassMappingRule : IGDConversionRule
    {
        public string Name => "inner-class-mapping";
        public int Priority => 10;
        public List<IGDConversionRuleMetadata> Metadata { get; set; } = [];

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
        public List<IGDConversionRuleMetadata> Metadata { get; set; } = [];

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
        public List<IGDConversionRuleMetadata> Metadata { get; set; } = [];

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
        public List<IGDConversionRuleMetadata> Metadata { get; set; } = [];

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
        public List<IGDConversionRuleMetadata> Metadata { get; set; } = [];

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
        public List<IGDConversionRuleMetadata> Metadata { get; set; } = [];

        public bool CanConvert(GDConversionNodeContext context) => context.Node is GDStatementsList;

        public GDConversionNodeResult Convert(GDConversionNodeContext context)
            => GDConversionNodeResult.Converted(SyntaxFactory.Block());
    }

    private sealed class PassMappingRule : IGDConversionRule
    {
        public string Name => "pass-mapping";
        public int Priority => 10;
        public List<IGDConversionRuleMetadata> Metadata { get; set; } = [];

        public bool CanConvert(GDConversionNodeContext context)
            => context.Node is GDExpressionStatement { Expression: GDPassExpression };

        public GDConversionNodeResult Convert(GDConversionNodeContext context)
            => GDConversionNodeResult.Converted(SyntaxFactory.EmptyStatement());
    }

    private static int Run(string cwd, string program, params ReadOnlySpan<string> args)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = cwd,
            FileName = program,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using (var process = Process.Start(startInfo))
        {
            if (process != null)
            {
                process.WaitForExit();
                return process.ExitCode;
            }
        }

        return int.MaxValue;
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(
            source,
            "*",
            SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(
            source,
            "*",
            SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, file);
            var destinationFile = Path.Combine(destination, relativePath);

            File.Copy(file, destinationFile);
        }
    }

    private static void ReplaceInFile(
        string path,
        string search,
        string replacement)
    {
        var contents = File.ReadAllText(path);
        contents = contents.Replace(search, replacement);
        File.WriteAllText(path, contents);
    }
}
