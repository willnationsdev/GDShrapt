using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GDShrapt.Converter.Planning;

public enum GDConversionNodeDisposition
{
    Converted,
    Ignored,
    Unmapped
}

/// <summary>
/// The explicit outcome of applying a node conversion rule.
/// </summary>
public sealed record GDConversionNodeResult
{
    private GDConversionNodeResult(
        GDConversionNodeDisposition disposition,
        SyntaxNode? csharpSyntax,
        GDConversionDestination? destination,
        string? ignoreReason)
    {
        Disposition = disposition;
        CSharpSyntax = csharpSyntax;
        Destination = destination;
        IgnoreReason = ignoreReason;
    }

    public GDConversionNodeDisposition Disposition { get; }
    public SyntaxNode? CSharpSyntax { get; }
    public GDConversionDestination? Destination { get; }
    public string? IgnoreReason { get; }

    /// <summary>
    /// Maps this GDScript syntax element to a C# syntax node, optionally declaring
    /// the destination type for the syntax contribution.
    /// </summary>
    public static GDConversionNodeResult Converted(SyntaxNode csharpSyntax, GDConversionDestination? destination = null)
    {
        ArgumentNullException.ThrowIfNull(csharpSyntax);
        if (destination != null && csharpSyntax is not MemberDeclarationSyntax)
            throw new ArgumentException(
                "A destination can only be assigned to a C# declaration contribution.",
                nameof(destination));
        return new GDConversionNodeResult(GDConversionNodeDisposition.Converted, csharpSyntax, destination, null);
    }

    public static GDConversionNodeResult Ignored(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new GDConversionNodeResult(GDConversionNodeDisposition.Ignored, null, null, reason);
    }

    internal static GDConversionNodeResult Unmapped { get; } =
        new(GDConversionNodeDisposition.Unmapped, null, null, null);

    internal GDConversionNodeResult WithCSharpSyntax(SyntaxNode syntax)
    {
        if (Disposition != GDConversionNodeDisposition.Converted)
            return this;

        return new GDConversionNodeResult(Disposition, syntax, Destination, IgnoreReason);
    }
}
