using GDShrapt.Converter.Planning;
using GDShrapt.Converter.Planning.Definitions;
using GDShrapt.Abstractions;
using GDShrapt.Reader;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.CodeAnalysis.CSharp;

namespace GDShrapt.Converter.Tests;

[TestClass]
[TestCategory("Integration")]
public sealed class GDConversionIntegrationTests
{
    private string _testRootDirectory = null!;
    private string _projectDirectory = null!;
    private string _scriptsDirectory = null!;

    [TestInitialize]
    public void Initialize()
    {
        var repoPath = Path.Combine(AppContext.BaseDirectory, "_repo");
        if (!Directory.Exists(repoPath))
            throw new DirectoryNotFoundException($"Converter integration fixture not found: {repoPath}");

        _testRootDirectory = Path.Combine(
            Path.GetTempPath(),
            "GDShrapt.Converter.IntegrationTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRootDirectory);
        CopyDirectory(repoPath, _testRootDirectory);

        _projectDirectory = Path.Combine(_testRootDirectory, "game", "client");
        _scriptsDirectory = Path.Combine(_projectDirectory, "scripts");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testRootDirectory))
            Directory.Delete(_testRootDirectory, recursive: true);
    }

    [TestMethod]
    public async Task GDSolutionContext_ResolvesAssemblyAliasesForDestinations()
    {
        using var analysis = await GDConversionAnalyzer.AnalyzeAsync(
            _projectDirectory,
            new GDConversionAnalysisOptions
            {
                FocusPath = "scripts",
                MaxDegreeOfParallelism = 1,
                EnrichCallSites = false
            });
        var solution = new GDSolutionContext(
            Path.Combine(_testRootDirectory, "game", "client", "TestGame.csproj"),
            "DebugTest",
            [
                $"ext:{GDSolutionContext.DefaultExtensionsAssemblyName}",
                $"tools:{GDSolutionContext.DefaultToolsAssemblyName}",
                GDSolutionContext.DefaultExtensionsAssemblyName
            ]);

        Assert.AreEqual(GDSolutionContext.DefaultExtensionsAssemblyName, solution.Project("ext").AssemblyName);
        Assert.AreEqual(GDSolutionContext.DefaultToolsAssemblyName, solution.Project("tools").AssemblyName);
        Assert.AreEqual(
            GDSolutionContext.DefaultExtensionsAssemblyName,
            solution.Project(GDSolutionContext.DefaultExtensionsAssemblyName).AssemblyName);
        Assert.AreEqual(
            "Godot.Extensions.csproj",
            Path.GetFileName(solution.Project("ext").ProjectFilePath));
        Assert.AreEqual(
            "Godot.Extensions.Tools.csproj",
            Path.GetFileName(solution.Project("tools").ProjectFilePath));

        var conversion = new GDConversionService(solution);
        var plan = conversion.CreatePlan(
            analysis,
            new GDConversionRuleSet(
            [
                new IntegrationClassMappingRule(),
                new IntegrationIgnoreNodeRule()
            ]));

        Assert.AreEqual("ext", plan.Files.Single().AssemblyName);
        var outputDirectory = Path.Combine(_projectDirectory, "generated");
        conversion.WriteOutputs(plan, outputDirectory);
        Assert.IsTrue(File.Exists(Path.Combine(outputDirectory, "utilities", "Extensions.cs")));
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, file);
            File.Copy(file, Path.Combine(destination, relativePath));
        }
    }

    private sealed class IntegrationClassMappingRule : IGDConversionRule
    {
        public string Name => "integration-class-mapping";
        public int Priority => 10;
        public List<IGDConversionRuleMetadata> Metadata { get; set; } = [];

        public bool CanConvert(GDConversionNodeContext context) => context.Node is GDClassDeclaration;

        public GDConversionNodeResult Convert(GDConversionNodeContext context)
            => GDConversionNodeResult.Converted(
                SyntaxFactory.ClassDeclaration("Extensions"),
                new GDConversionDestination(
                    "Project.Utilities.Extensions",
                    "ext",
                    Path.Combine("utilities", "Extensions.cs")));
    }

    private sealed class IntegrationIgnoreNodeRule : IGDConversionRule
    {
        public string Name => "integration-ignore-fallback";
        public int Priority => 0;
        public List<IGDConversionRuleMetadata> Metadata { get; set; } = [];

        public bool CanConvert(GDConversionNodeContext context) => true;

        public GDConversionNodeResult Convert(GDConversionNodeContext context)
            => GDConversionNodeResult.Ignored($"No C# syntax is needed for {context.Syntax.TypeName}.");
    }
}
