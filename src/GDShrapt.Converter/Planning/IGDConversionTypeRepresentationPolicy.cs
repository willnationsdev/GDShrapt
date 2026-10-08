using GDShrapt.Abstractions;
using GDShrapt.Reader;

namespace GDShrapt.Converter.Planning;

/// <summary>
/// Selects a native C# representation for a use site when flow and API-boundary
/// analysis determines that a Godot representation is unnecessary.
/// </summary>
public interface IGDConversionTypeRepresentationPolicy
{
    GDNativeTypeRepresentation? GetNativeTypeName(
        string godotTypeName,
        GDSemanticType? semanticType,
        GDNode useSite,
        GDConversionNodeContext context);
}

public enum GDConversionRealPrecision
{
    None,
    Godot,
    Single,
    Double
}

/// <summary>
/// C# type syntax selected for one Godot type use site.
/// </summary>
public sealed record GDNativeTypeRepresentation(
    string TypeName,
    GDConversionRealPrecision ArgumentPrecision = GDConversionRealPrecision.None);
