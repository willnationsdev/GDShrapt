using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GDShrapt.Converter.Planning;

/// <summary>
/// A rule's proposal for a higher-level conversion stage to evaluate.
/// </summary>
public interface IGDConversionContribution
{
    string Kind { get; }
    string Key { get; }

    /// <summary>
    /// Resolves syntax whose representation depends on whether this suggestion is adopted.
    /// Contributions without conditional syntax changes may return the input unchanged.
    /// </summary>
    SyntaxNode ResolveSyntax(SyntaxNode syntax, bool adopted) => syntax;
}

/// <summary>
/// Proposes a namespace import and defers syntax selection until the containing
/// output file decides whether to adopt that import.
/// </summary>
public sealed record GDConversionNamespaceUsingContribution(string Namespace)
    : IGDConversionContribution
{
    public string Kind => "namespace-using";
    public string Key => Namespace;

    public SyntaxNode ResolveSyntax(SyntaxNode syntax, bool adopted)
        => adopted
            ? new GDNamespaceUsingRewriter(Namespace).Visit(syntax) ?? syntax
            : syntax;

    private sealed class GDNamespaceUsingRewriter(string namespaceName) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node)
        {
            var isNestedNamespacePrefix = node.Parent is QualifiedNameSyntax parent &&
                ReferenceEquals(parent.Left, node);
            if (node.Left.ToString() == namespaceName && !isNestedNamespacePrefix)
            {
                return node.Right.WithTriviaFrom(node);
            }
            return base.VisitQualifiedName(node);
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var isNestedNamespacePrefix = node.Parent is MemberAccessExpressionSyntax parent &&
                ReferenceEquals(parent.Expression, node);
            if (node.Expression.ToString() == namespaceName && !isNestedNamespacePrefix)
            {
                return node.Name.WithTriviaFrom(node);
            }
            return base.VisitMemberAccessExpression(node);
        }
    }
}
