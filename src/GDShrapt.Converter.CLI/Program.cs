using ConsoleAppFramework;
using GDShrapt.Abstractions;
using GDShrapt.Converter;
using GDShrapt.Converter.Planning;
using GDShrapt.Semantics;
using Godot;
using System.Text.Json;

var app = ConsoleApp.Create();

app.Add<BasicCommands>();

app.Run(args);

public class BasicCommands
{
    private static GDConversionAnalysisOptions CreateDefaultAnalysisOptions(string projectRoot, string? focusPath) => new GDConversionAnalysisOptions
    {
        EnableParallelAnalysis = true,
        // The JSON artifact contains script metadata, not inferred call-site types.
        EnrichCallSites = true,
        ItemProgressStart = progress =>
        {
            var relativePath = string.IsNullOrEmpty(progress.CurrentFile)
                ? string.Empty
                : Path.GetRelativePath(projectRoot, progress.CurrentFile);
            Console.WriteLine($"START [{progress.CompletedFiles:D4} of {progress.TotalFiles:D4}]: {relativePath}");
        },
        FocusPath = focusPath,
        Logger = GDConsoleLogger.Instance,
    };

    /// <summary>
    /// Produces a JSON analysis artifact for all scripts in the specified project.
    /// </summary>
    /// <param name="path">The project directory or a directory inside the project.</param>
    /// <param name="output">Optional output JSON path. Defaults to gdshrapt-analysis.json in the project root.</param>
    public async Task Analyze([Argument] string path = ".", string? output = null)
    {
        var projectRoot = GDProjectLoader.FindProjectRoot(path) ?? Path.GetFullPath(path);
        GDConversionAnalysis? analysis = null;

        try
        {
            analysis = await GDConversionAnalyzer.AnalyzeAsync(path, CreateDefaultAnalysisOptions(projectRoot, path));

            var artifact = new
            {
                schemaVersion = 1,
                projectPath = analysis.Project.ProjectPath,
                godotVersion = analysis.Project.GodotVersion?.ToString(),
                autoloads = analysis.Project.AutoloadEntries.Select(entry => new
                {
                    entry.Name,
                    entry.Path,
                    entry.Enabled
                }),
                scripts = analysis.Scripts.Select(script => new
                {
                    path = script.ResPath ?? script.FullPath,
                    script.TypeName,
                    script.IsGlobal,
                    script.WasReadError,
                    hasSyntaxTree = script.Class != null,
                    hasSemanticModel = script.SemanticModel != null
                })
            };

            Console.WriteLine($"Analyzed {analysis.Scripts.Count} scripts.");

            var outputPath = string.IsNullOrWhiteSpace(output)
                ? (Directory.Exists(path) ? path : analysis.Project.ProjectPath).PathJoin("gdshrapt-analysis.json")
                : Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(artifact, new JsonSerializerOptions
            {
                WriteIndented = true
            }));

            Console.WriteLine($"Analysis artifact: {outputPath}");
        }
        finally
        {
            analysis?.Dispose();
        }
    }

    /// <summary>
    /// Plans syntax conversions and writes mapped type declarations when the plan is complete.
    /// </summary>
    /// <param name="path">The project directory or a directory inside the project.</param>
    /// <param name="outputPath">The output directory for generated C# files, relative to the Godot project directory when not rooted.</param>
    /// <param name="dryRun">Report planned files without writing them.</param>
    public async Task Convert([Argument] string path, [Argument] string outputPath, bool dryRun)
    {
        var projectRoot = GDProjectLoader.FindProjectRoot(path) ?? Path.GetFullPath(path);
        using var analysis = await GDConversionAnalyzer.AnalyzeAsync(path, CreateDefaultAnalysisOptions(projectRoot, path));
        var resolvedOutputPath = Path.GetFullPath(outputPath, analysis.Project.ProjectPath);
        var conversionService = new GDConversionService();
        var plan = conversionService.CreatePlan(analysis, GDConversionRuleSet.Empty);

        Console.WriteLine($"Analyzed {analysis.Scripts.Count} scripts.");
        foreach (var entry in plan.Entries)
        {
            var converted = entry.Mappings.Count(mapping => mapping.Result.Disposition == GDConversionNodeDisposition.Converted);
            var ignored = entry.Mappings.Count(mapping => mapping.Result.Disposition == GDConversionNodeDisposition.Ignored);
            var unmapped = entry.UnmappedNodes.Count();
            Console.WriteLine($"{entry.SourcePath}: converted={converted}, ignored={ignored}, unmapped={unmapped}");
        }

        if (!plan.IsComplete)
        {
            var message = $"Conversion plan is incomplete: {plan.UnmappedNodes.Count()} syntax elements are unmapped.";
            if (!dryRun)
                throw new InvalidOperationException(message);
            Console.WriteLine(message);
            return;
        }

        foreach (var file in plan.Files)
            Console.WriteLine(Path.GetFullPath(Path.Combine(resolvedOutputPath, file.OutputPath)));

        if (dryRun)
            return;

        conversionService.WriteOutputs(plan, resolvedOutputPath);
        Console.WriteLine($"Wrote {plan.Files.Count} generated C# files to {resolvedOutputPath}.");
    }
}