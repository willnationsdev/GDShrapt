namespace GDShrapt.Converter.Planning;

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
