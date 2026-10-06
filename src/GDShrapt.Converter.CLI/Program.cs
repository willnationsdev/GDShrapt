using ConsoleAppFramework;
using GDShrapt.Converter;
using Godot;
using System.Collections.Concurrent;
using System.Text.Json;

var app = ConsoleApp.Create();

app.Add<BasicCommands>();

app.Run(args);

public class BasicCommands
{
    /// <summary>
    /// Produces a JSON analysis artifact for all scripts in the specified project.
    /// </summary>
    /// <param name="path">The project directory or a directory inside the project.</param>
    /// <param name="output">Optional output JSON path. Defaults to gdshrapt-analysis.json in the project root.</param>
    public async Task Analyze([Argument] string path = ".", string? output = null)
    {
        var progressBarOptions = new ShellProgressBar.ProgressBarOptions
        {
            ForegroundColor = ConsoleColor.Yellow,
            BackgroundColor = ConsoleColor.DarkYellow,
            ProgressCharacter = '-',
        };
        var childProgressBarOptions = new ShellProgressBar.ProgressBarOptions
        {
            ForegroundColor = ConsoleColor.Green,
            BackgroundColor = ConsoleColor.DarkGreen,
            ProgressCharacter = '-',
        };
        ShellProgressBar.ProgressBar? progress = null;
        ConcurrentDictionary<int, ShellProgressBar.ChildProgressBar> childProgressBars = new();
        GDConversionAnalysis? analysis = null;

        try
        {
            analysis = await GDConversionAnalyzer.AnalyzeAsync(path, new GDConversionAnalysisOptions
            {
                EnableParallelAnalysis = false,
                ProgressStarting = SetupProgress,
                ItemProgressStart = ReportProgressStart,
                ItemProgressEnd = ReportProgress,
            });

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

            var outputPath = string.IsNullOrWhiteSpace(output)
                ? Path.Combine(analysis.Project.ProjectPath, "gdshrapt-analysis.json")
                : Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(artifact, new JsonSerializerOptions
            {
                WriteIndented = true
            }));

            Console.WriteLine($"Analyzed {analysis.Scripts.Count} scripts.");
            Console.WriteLine($"Analysis artifact: {outputPath}");
        }
        finally
        {
            foreach (var childProgressBar in childProgressBars.Values)
            {
                childProgressBar.Dispose();
            }
            progress?.Dispose();
            analysis?.Dispose();
            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.White;
        }

        void SetupProgress(int total)
        {
            progress = new ShellProgressBar.ProgressBar(total, $"Scanning files... [0 of {total}]", progressBarOptions);
        }

        void ReportProgressStart(GDShrapt.Semantics.AnalysisProgress ap)
        {
            var i = ap.CompletedFiles + 1;
            var msg = $"Start {i} of {ap.TotalFiles} {Console.CursorTop}/{Console.WindowHeight}: {ap.CurrentFile}";
            var child = progress!.Spawn(1, msg, childProgressBarOptions);
            childProgressBars[ap.CompletedFiles] = child;
            child.Tick($"End {i} of {ap.TotalFiles} {Console.CursorTop}/{Console.WindowHeight}: {ap.CurrentFile}");
            var progressMsg = $"Scanning files... [{i} of {ap.TotalFiles}]";
            progress!.Tick(progressMsg);
        }

        void ReportProgress(GDShrapt.Semantics.AnalysisProgress ap)
        {
            if (childProgressBars.TryGetValue(ap.CompletedFiles, out var child))
            {
                var i = ap.CompletedFiles + 1;
                child.Tick($"End {i} of {ap.TotalFiles} {Console.CursorTop}/{Console.WindowHeight}: {ap.CurrentFile}");
                var progressMsg = $"Scanning files... [{i} of {ap.TotalFiles}]";
                progress!.Tick(progressMsg);
            }
        }
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="path">The project directory or a directory inside the project.</param>
    /// <param name="output">The output folder for generated, converted C# files.</param>
    /// <param name="dryRun">Print planned outputs without writing files.</param>
    public async Task Convert([Argument] string path, [Argument] string output, bool dryRun)
    {
        using var analysis = await GDConversionAnalyzer.AnalyzeAsync(path);
        var conversionService = new GDConversionService();
        var plan = conversionService.CreatePlan(analysis, GDConversionRuleSet.Empty);

        Console.WriteLine($"Analyzed {analysis.Scripts.Count} scripts.");
        if (dryRun)
        {
            foreach (var entry in plan.Entries.Where(entry => entry.MatchingRules.Count > 1))
            {
                Console.WriteLine($"{entry.SourcePath}: selected '{entry.SelectedRule}' from {string.Join(", ", entry.MatchingRules)}");
            }

            foreach (var generated in plan.Outputs)
                Console.WriteLine(Path.GetFullPath(Path.Combine(output, generated.RelativePath)));
        }
        else
        {
            conversionService.WriteOutputs(plan, output);
            Console.WriteLine($"Wrote {plan.Outputs.Count()} generated files to {Path.GetFullPath(output)}.");
        }

        if (plan.Outputs.Count() == 0)
            Console.WriteLine("No conversion rules produced output. Register IGDConversionRule implementations to enable conversion.");
    }
}