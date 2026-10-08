namespace GDShrapt.Converter.Planning;

public sealed class DefaultGDConversionFormatter(GDConversionFormattingOptions options) : IGDConversionFormatter
{
    public GDConversionFormattingOptions Options { get; } = options;

    public string Name(string name) => Options.FieldNameStyle switch
    {
        GDConversionNameStyle.PascalCase => NameHelper.ToPascalCase(name),
        GDConversionNameStyle.CamelCase => NameHelper.ToCamelCase(name),
        GDConversionNameStyle.PrivatePascalCase => "_" + NameHelper.ToPascalCase(name),
        _ => NameHelper.ToPrivateField(name)
    };

    public string Name(string name, GDConversionNamingContext context)
    {
        var rule = Options.NamingRules
            .Where(candidate => candidate.AppliesTo(context))
            .OrderByDescending(candidate => candidate.RequiredModifiers.Count)
            .ThenByDescending(candidate => candidate.ApplicableAccessibilities.Count(accessibility => accessibility != "*"))
            .ThenByDescending(candidate => candidate.ApplicableKinds.Count(kind => kind != "*"))
            .ThenBy(candidate => candidate.Order)
            .FirstOrDefault();
        return rule == null ? Name(name) : rule.Style.Apply(name);
    }
}
