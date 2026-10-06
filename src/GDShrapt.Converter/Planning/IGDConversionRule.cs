namespace GDShrapt.Converter.Planning;

public interface IGDConversionRule
{
    string Name { get; }
    int Priority { get; }
    bool CanConvert(GDConversionContext context);
    IEnumerable<GDConversionOutput> Convert(GDConversionContext context);
}
