using GDShrapt.Reader;

namespace GDShrapt.Converter.Planning;

/// <summary>
/// Converts or explicitly ignores one syntax node or terminal token. Rules are
/// evaluated for every syntax element in a script's syntax tree, in priority order.
/// </summary>
public interface IGDConversionRule
{
    string Name { get; }
    int Priority { get; }
    bool CanConvert(GDConversionNodeContext context);
    GDConversionNodeResult Convert(GDConversionNodeContext context);
}
