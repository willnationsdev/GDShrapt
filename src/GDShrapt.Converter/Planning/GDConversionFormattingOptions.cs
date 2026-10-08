namespace GDShrapt.Converter.Planning;

/// <summary>
/// Formatting and identifier conventions resolved for one C# project.
/// </summary>
public sealed class GDConversionFormattingOptions
{
    public bool UseTabIndent { get; init; }
    public int IndentSize { get; init; } = 4;
    public string LineEnding { get; init; } = Environment.NewLine;
    public string? GitAttributesRoot { get; init; }
    public bool PreferFileScopedNamespaces { get; init; }
    public GDConversionNameStyle FieldNameStyle { get; init; } = GDConversionNameStyle.PrivateCamelCase;
    public IReadOnlyList<GDConversionNamingRule> NamingRules { get; init; } = [];

    public string GetLineEnding(string outputPath)
        => GitAttributesRoot == null
            ? LineEnding
            : GDConversionGitAttributes.GetLineEnding(outputPath, GitAttributesRoot, LineEnding);
}

public enum GDConversionNameStyle
{
    PascalCase,
    CamelCase,
    PrivateCamelCase,
    PrivatePascalCase
}

public sealed record GDConversionNamingRule(
    IReadOnlySet<string> ApplicableKinds,
    IReadOnlySet<string> ApplicableAccessibilities,
    IReadOnlySet<string> RequiredModifiers,
    IReadOnlySet<string> ForbiddenModifiers,
    GDConversionNamingStyle Style,
    int Order)
{
    public bool AppliesTo(GDConversionNamingContext context)
        => (ApplicableKinds.Contains("*") || ApplicableKinds.Contains(context.Kind)) &&
           (ApplicableAccessibilities.Contains("*") || ApplicableAccessibilities.Contains(context.Accessibility)) &&
           RequiredModifiers.All(context.Modifiers.Contains) &&
           ForbiddenModifiers.All(modifier => !context.Modifiers.Contains(modifier));
}

public sealed record GDConversionNamingStyle(
    string Capitalization,
    string RequiredPrefix,
    string RequiredSuffix,
    string WordSeparator)
{
    public string Apply(string name)
    {
        var words = System.Text.RegularExpressions.Regex
            .Replace(name, "([a-z0-9])([A-Z])", "$1 $2")
            .Split(['_', '-', ' ', '.'], StringSplitOptions.RemoveEmptyEntries);
        var cased = Capitalization.ToLowerInvariant() switch
        {
            "pascal_case" or "pascalcase" => string.Concat(words.Select(NameHelper.ToPascalCase)),
            "camel_case" or "camelcase" => NameHelper.ToCamelCase(string.Concat(words.Select(NameHelper.ToPascalCase))),
            "first_word_upper" => UpperFirst(string.Concat(words.Select(NameHelper.ToPascalCase))),
            "all_upper" or "uppercase" => string.Join(WordSeparator, words).ToUpperInvariant(),
            "all_lower" or "lowercase" => string.Join(WordSeparator, words).ToLowerInvariant(),
            _ => name
        };
        return RequiredPrefix + cased + RequiredSuffix;
    }

    private static string UpperFirst(string value)
        => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}

public sealed record GDConversionNamingContext(
    string Kind,
    string Accessibility,
    IReadOnlySet<string> Modifiers);
