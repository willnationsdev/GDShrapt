using GDShrapt.Reader;
using Microsoft.CodeAnalysis.CSharp;

namespace GDShrapt.Converter.Planning;

/// <summary>
/// A tag interface that allows arbitrary metadata to be associated with conversion rules.
/// </summary>
public interface IGDConversionRuleMetadata;

public interface IGDSyntaxTransformationRuleMetadata : IGDConversionRuleMetadata
{
    public T Transform<T>(GDConversionNodeContext context, T syntax) where T : CSharpSyntaxNode;
}

/// <summary>
/// Converts or explicitly ignores one syntax node or terminal token. Rules are
/// evaluated for every syntax element in a script's syntax tree, in priority order.
/// </summary>
public interface IGDConversionRule
{
    /// <summary>
    /// The name that identifies this rule type without API dependencies.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The priority of this rule compared to competing rules trying to convert the same GDScript syntax tokens.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Whether this rule can convert a given subtree of GDScript syntax.
    /// </summary>
    /// <param name="context">The node context.</param>
    /// <returns><c>true</c> if it can convert the syntax. Otherwise <c>false</c>.</returns>
    bool CanConvert(GDConversionNodeContext context);

    /// <summary>
    /// Converts a given subtree of GDScript syntax into its equivalent C# syntax. May potentially contribute towards multiple output file destinations.
    /// </summary>
    /// <param name="context">The node context.</param>
    /// <returns>The resulting syntax/file contributions published by this rule.</returns>
    GDConversionNodeResult Convert(GDConversionNodeContext context);

    /// <summary>
    /// The list of metadata tags associated with the rule. This allows the calling context to attach customization details that it would like this rule to respect while converting GDScript syntax.
    /// </summary>
    List<IGDConversionRuleMetadata> Metadata { get; set; }

    /// <summary>
    /// A helper function to facilitate building rules with specific metadata.
    /// </summary>
    /// <param name="metadata"></param>
    /// <returns></returns>
    IGDConversionRule With(params ReadOnlySpan<IGDConversionRuleMetadata> metadata)
    {
        Metadata.AddRange(metadata);
        return this;
    }
}
