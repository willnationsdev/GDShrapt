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
    public ISolutionContext Solution => Conversion.Solution;
    public GDConversionAnalysis Analysis => Conversion.Analysis;
    public GDScriptFile Script => Conversion.Script;
    public GDClassDeclaration? Class => Conversion.Class;
    public GDSemanticModel? SemanticModel => Conversion.SemanticModel;
    public GDSyntaxToken Syntax { get; }
    public GDNode? Node => Syntax as GDNode;
    public GDNode? Parent => Syntax.Parent;
    public IReadOnlyList<GDConversionNodeMapping> ChildMappings { get; }
    private readonly List<IGDConversionContribution> _contributions = [];
    internal IReadOnlyList<IGDConversionContribution> Contributions => _contributions;

    internal void Suggest(IGDConversionContribution contribution)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        if (!_contributions.Any(existing => existing.Kind == contribution.Kind && existing.Key == contribution.Key))
            _contributions.Add(contribution);
        Conversion.Suggest(contribution);
    }

    public GDNativeTypeRepresentation? GetNativeTypeName(
        string godotTypeName,
        GDSemanticType? semanticType = null,
        GDNode? useSite = null)
        => Conversion.TypeRepresentationPolicy?.GetNativeTypeName(
            godotTypeName,
            semanticType,
            useSite ?? Node ?? Parent ?? throw new InvalidOperationException("The syntax element has no AST node context."),
            this);

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
