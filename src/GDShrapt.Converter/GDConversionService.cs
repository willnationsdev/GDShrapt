using GDShrapt.Semantics;

namespace GDShrapt.Converter;

public sealed class GDConversionContext(GDConversionAnalysis analysis, GDScriptFile script)
{
    public GDConversionAnalysis Analysis { get; } = analysis;
    public GDScriptFile Script { get; } = script;
}

public sealed record GDConversionOutput(string RelativePath, string Content);

public interface IGDConversionRule
{
    string Name { get; }
    int Priority { get; }
    bool CanConvert(GDConversionContext context);
    IEnumerable<GDConversionOutput> Convert(GDConversionContext context);
}

/// <summary>
/// Ordered conversion rules. Higher priority rules are considered first; registration
/// order breaks ties deterministically.
/// </summary>
public sealed class GDConversionRuleSet
{
    private readonly IGDConversionRule[] _rules;

    public GDConversionRuleSet(IEnumerable<IGDConversionRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules
            .Select((rule, index) => (Rule: rule ?? throw new ArgumentException("Rules cannot contain null entries.", nameof(rules)), Index: index))
            .OrderByDescending(entry => entry.Rule.Priority)
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Rule)
            .ToArray();
    }

    public static GDConversionRuleSet Empty { get; } = new(Array.Empty<IGDConversionRule>());
    public IReadOnlyList<IGDConversionRule> Rules => _rules;
}

public sealed record GDConversionPlanEntry(
    string SourcePath,
    string? SelectedRule,
    IReadOnlyList<string> MatchingRules,
    IReadOnlyList<GDConversionOutput> Outputs);

public sealed class GDConversionPlan(IReadOnlyList<GDConversionPlanEntry> entries)
{
    public IReadOnlyList<GDConversionPlanEntry> Entries { get; } = entries;
    public IEnumerable<GDConversionOutput> Outputs => Entries.SelectMany(entry => entry.Outputs);
}

/// <summary>
/// Applies configured rules to analyzed scripts and optionally writes their outputs.
/// </summary>
public sealed class GDConversionService
{
    public GDConversionPlan CreatePlan(GDConversionAnalysis analysis, GDConversionRuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(rules);

        var entries = new List<GDConversionPlanEntry>(analysis.Scripts.Count);
        foreach (var script in analysis.Scripts)
        {
            var context = new GDConversionContext(analysis, script);
            var matchingRules = rules.Rules.Where(rule => rule.CanConvert(context)).ToArray();
            var selectedRule = matchingRules.FirstOrDefault();
            var outputs = selectedRule?.Convert(context).ToArray() ?? [];
            var sourcePath = script.FullPath ?? script.ResPath ?? script.TypeName ?? "<unknown>";

            entries.Add(new GDConversionPlanEntry(
                sourcePath,
                selectedRule?.Name,
                matchingRules.Select(rule => rule.Name).ToArray(),
                outputs));
        }

        EnsureUniqueOutputPaths(entries);
        return new GDConversionPlan(entries);
    }

    public void WriteOutputs(GDConversionPlan plan, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var rootPath = Path.GetFullPath(outputDirectory);
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var outputPaths = new HashSet<string>(pathComparer);
        var pendingWrites = new List<(string Path, string Content)>();
        foreach (var output in plan.Outputs)
        {
            var outputPath = Path.GetFullPath(Path.Combine(rootPath, output.RelativePath));
            var relativePath = Path.GetRelativePath(rootPath, outputPath);
            if (Path.IsPathRooted(output.RelativePath) || relativePath == ".." ||
                relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Conversion output escapes the output directory: {output.RelativePath}");
            }

            if (!outputPaths.Add(outputPath))
                throw new InvalidOperationException($"Multiple conversion rules produced the same output path: {output.RelativePath}");

            pendingWrites.Add((outputPath, output.Content));
        }

        foreach (var (outputPath, content) in pendingWrites)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, content);
        }
    }

    private static void EnsureUniqueOutputPaths(IEnumerable<GDConversionPlanEntry> entries)
    {
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var paths = new HashSet<string>(pathComparer);
        foreach (var output in entries.SelectMany(entry => entry.Outputs))
        {
            if (string.IsNullOrWhiteSpace(output.RelativePath))
                throw new InvalidOperationException("A conversion rule produced an empty output path.");

            if (!paths.Add(Path.GetFullPath(output.RelativePath)))
                throw new InvalidOperationException($"Multiple conversion rules produced the same output path: {output.RelativePath}");
        }
    }
}