using GDShrapt.Abstractions;
using GDShrapt.Semantics;

namespace GDShrapt.Converter;

public sealed record GDConversionAnalysisOptions
{
    public bool IncludeSceneTypes { get; init; }
    public bool EnrichCallSites { get; init; } = true;
    public bool EnableParallelAnalysis { get; init; } = true;
    public int MaxDegreeOfParallelism { get; init; } = -1;

    /// <summary>
    /// An optional directory path that limits semantic analysis to files in that directory and its subdirectories.
    /// Relative paths are resolved from the project root.
    /// </summary>
    public string? FocusPath { get; init; }

    /// <summary>
    /// An optional callback invoked to setup progress UI for the analyzing process.
    /// </summary>
    public Action<int>? ProgressStarting { get; set; }

    /// <summary>
    /// An optional callback invoked to iterate progress UI for the analyzing process, marking the start of a new item.
    /// </summary>
    public Action<AnalysisProgress>? ItemProgressStart { get; set; }

    /// <summary>
    /// An optional callback invoked to iterate progress UI for the analyzing process, marking the completion of an item.
    /// </summary>
    public Action<AnalysisProgress>? ItemProgressEnd { get; set; }

    public IGDLogger? Logger { get; set; }
}

/// <summary>
/// Owns the loaded project and its cross-file semantic analysis results.
/// Dispose the result when conversion and artifact generation are complete.
/// </summary>
public sealed class GDConversionAnalysis : IDisposable
{
    private readonly GDScriptFile[] _scripts;

    internal GDConversionAnalysis(GDScriptProject project, GDConversionAnalysisOptions options)
    {
        Project = project;
        Options = options;
        _scripts = project.ScriptFiles
            .OrderBy(script => script.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public GDScriptProject Project { get; }
    public GDConversionAnalysisOptions Options { get; }
    public IReadOnlyList<GDScriptFile> Scripts => _scripts;

    public void Dispose() => Project.Dispose();
}

/// <summary>
/// Loads all scripts in a project before running semantic analysis, so global classes,
/// autoloads, and cross-file references are available regardless of file order.
/// </summary>
public static class GDConversionAnalyzer
{
    public static async Task<GDConversionAnalysis> AnalyzeAsync(
        string projectPath,
        GDConversionAnalysisOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        var fullPath = Path.GetFullPath(projectPath);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Project directory not found: {fullPath}");

        var rootPath = GDProjectLoader.FindProjectRoot(fullPath) ?? fullPath;
        options ??= new GDConversionAnalysisOptions();
        if (options.MaxDegreeOfParallelism < -1)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxDegreeOfParallelism must be -1, 0, or a positive value.");
        var semanticsConfig = new GDSemanticsConfig
        {
            EnableIncrementalAnalysis = false,
            EnableIncrementalParsing = false,
            EnableParallelAnalysis = options.EnableParallelAnalysis,
            MaxDegreeOfParallelism = options.MaxDegreeOfParallelism
        };
        var project = new GDScriptProject(
            new GDDefaultProjectContext(rootPath),
            new GDScriptProjectOptions
            {
                EnableFileWatcher = false,
                EnableSceneTypesProvider = options.IncludeSceneTypes,
                EnableCallSiteRegistry = false,
                EnableSceneChangeReanalysis = false,
                FocusPath = options.FocusPath,
                SemanticsConfig = semanticsConfig,
                ProgressStarting = options.ProgressStarting,
                ItemProgressStart = options.ItemProgressStart,
                ItemProgressEnd = options.ItemProgressEnd,
                Logger = options.Logger,
            });

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                project.LoadScripts(cancellationToken);
                if (options.IncludeSceneTypes)
                    project.LoadScenes();
            }, cancellationToken).ConfigureAwait(false);

            await project.AnalyzeAllAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (options.EnrichCallSites)
                project.EnrichWithCallSiteAnalysis();

            return new GDConversionAnalysis(project, options);
        }
        catch
        {
            project.Dispose();
            throw;
        }
    }
}