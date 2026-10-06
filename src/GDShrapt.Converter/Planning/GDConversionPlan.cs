namespace GDShrapt.Converter.Planning;

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

