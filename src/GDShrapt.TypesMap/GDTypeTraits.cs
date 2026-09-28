namespace GDShrapt.TypesMap;

public class GDTypeTraits
{
    public string[]? ImplicitlyConvertibleTo { get; set; }
    public bool IsPackedArray { get; set; }
    public string? PackedElementType { get; set; }
    public bool IsNumeric { get; set; }
    public bool IsIterable { get; set; }
    public bool IsIndexable { get; set; }
    public bool IsNullable { get; set; }
    public bool IsVector { get; set; }
    public bool IsContainer { get; set; }
    public string? FloatVariant { get; set; }
}
