using GDShrapt.Abstractions;
using GDShrapt.Reader;
using GDShrapt.Semantics;

namespace GDShrapt.Converter.Planning;

/// <summary>
/// Per-node view of the script and its semantic/flow analysis.
/// </summary>
public sealed class GDConversionNodeContext
{
    internal GDConversionNodeContext(
        GDConversionContext conversion,
        GDSyntaxToken syntax,
        IReadOnlyList<GDConversionNodeMapping> childMappings)
    {
        Conversion = conversion;
        Syntax = syntax;
        ChildMappings = childMappings;
    }

    public GDConversionContext Conversion { get; }
    public GDConversionAnalysis Analysis => Conversion.Analysis;
    public GDScriptFile Script => Conversion.Script;
    public GDClassDeclaration? Class => Conversion.Class;
    public GDSemanticModel? SemanticModel => Conversion.SemanticModel;
    public GDSyntaxToken Syntax { get; }
    public GDNode? Node => Syntax as GDNode;
    public GDNode? Parent => Syntax.Parent as GDNode;
    public IReadOnlyList<GDConversionNodeMapping> ChildMappings { get; }

    /// <summary>
    /// Gets the flow-sensitive variable type at this node or another node in the same script.
    /// Returns null when semantic or flow information is unavailable.
    /// </summary>
    public GDFlowVariableType? GetVariableTypeAt(string variableName, GDNode? location = null)
        => SemanticModel?.GetVariableTypeAt(variableName, location ?? GetDefaultLocation());

    /// <summary>
    /// Gets the flow state at this node or another node in the same script.
    /// Returns null when semantic or flow information is unavailable.
    /// </summary>
    public GDFlowState? GetFlowStateAtLocation(GDNode? location = null)
        => SemanticModel?.GetFlowStateAtLocation(location ?? GetDefaultLocation());

    private GDNode GetDefaultLocation()
        => Node ?? Parent ?? throw new InvalidOperationException("The syntax element has no AST node context.");
}
