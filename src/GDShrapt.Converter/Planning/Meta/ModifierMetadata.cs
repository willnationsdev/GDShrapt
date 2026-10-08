using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Runtime.CompilerServices;

namespace GDShrapt.Converter.Planning.Meta;

/// <summary>
/// Indicates a declaration should have a given modifier associated with it.
/// </summary>
/// <param name="kind">A syntax kind value for a token. These have the suffix Token or Keyword.</param>
public sealed class ModifierMetadata(SyntaxKind kind) : IGDSyntaxTransformationRuleMetadata
{
    public SyntaxKind Kind { get; } = kind;
    public SyntaxToken Token => SyntaxFactory.Token(Kind);

    public static ModifierMetadata ReadOnly { get; } = new ModifierMetadata(SyntaxKind.ReadOnlyKeyword);
    public static ModifierMetadata Public { get; } = new ModifierMetadata(SyntaxKind.PublicKeyword);
    public static ModifierMetadata Protected { get; } = new ModifierMetadata(SyntaxKind.ProtectedKeyword);
    public static ModifierMetadata Private { get; } = new ModifierMetadata(SyntaxKind.PrivateKeyword);
    public static ModifierMetadata Internal { get; } = new ModifierMetadata(SyntaxKind.InternalKeyword);
    public static ModifierMetadata Abstract { get; } = new ModifierMetadata(SyntaxKind.AbstractKeyword);
    public static ModifierMetadata Virtual { get; } = new ModifierMetadata(SyntaxKind.VirtualKeyword);
    public static ModifierMetadata Sealed { get; } = new ModifierMetadata(SyntaxKind.SealedKeyword);
    public static ModifierMetadata Override { get; } = new ModifierMetadata(SyntaxKind.OverrideKeyword);

    public T Transform<T>(GDConversionNodeContext context, T syntax) where T : CSharpSyntaxNode
    {
        // "MemberDeclarationSyntax" is the base type for not only member declarations, but also ANY kind of TypeDeclarationSyntax (class, record, etc.).
        if (syntax is MemberDeclarationSyntax mds) return Unsafe.As<T>(mds.AddModifiers(Token));
        return syntax;
    }
}
