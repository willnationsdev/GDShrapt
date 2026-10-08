namespace GDShrapt.Converter.Planning;

/// <summary>
/// Provides preferred formatting helper methods for conversion rules.
/// </summary>
public interface IGDConversionFormatter
{
    GDConversionFormattingOptions Options { get; }

    string Name(string name);

    string Name(string name, GDConversionNamingContext context);
}
