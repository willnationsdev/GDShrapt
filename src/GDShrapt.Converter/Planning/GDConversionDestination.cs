namespace GDShrapt.Converter.Planning;

/// <summary>
/// Identifies the C# type that receives a syntax contribution and optionally
/// overrides its assembly, output path, nested type, or contribution order.
/// </summary>
public sealed record GDConversionDestination(
    string FullName,
    string? AssemblyName = null,
    string? RelativePath = null,
    string? InnerClassName = null,
    int Order = 0)
{
    public string FullName { get; } = string.IsNullOrWhiteSpace(FullName)
        ? throw new ArgumentException("A destination type name is required.", nameof(FullName))
        : FullName;

    public string Namespace => FullName[..FullName.IndexOf('.')];
    public string TypeName => FullName[(FullName.IndexOf('.') + 1)..];

    public string? AssemblyName { get; init; } =
        string.IsNullOrWhiteSpace(AssemblyName) ? null : AssemblyName;

    public string? RelativePath { get; init; } =
        string.IsNullOrWhiteSpace(RelativePath) ? null : RelativePath;

    public string? InnerClassName { get; init; } =
        string.IsNullOrWhiteSpace(InnerClassName) ? null : InnerClassName;
}
