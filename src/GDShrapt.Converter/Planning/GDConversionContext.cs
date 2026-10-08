using GDShrapt.Abstractions;
using GDShrapt.Reader;
using GDShrapt.Semantics;

namespace GDShrapt.Converter.Planning;

public sealed class GDConversionContext(
    GDConversionAnalysis analysis,
    GDScriptFile script,
    ISolutionContext solution,
    IGDConversionTypeRepresentationPolicy? typeRepresentationPolicy)
{
    private readonly List<IGDConversionContribution> _suggestedContributions = [];

    public GDConversionAnalysis Analysis { get; } = analysis;
    public GDScriptFile Script { get; } = script;
    public ISolutionContext Solution { get; } = solution;
    public IGDConversionTypeRepresentationPolicy? TypeRepresentationPolicy { get; } = typeRepresentationPolicy;
    internal IReadOnlyList<IGDConversionContribution> SuggestedContributions => _suggestedContributions;

    internal void Suggest(IGDConversionContribution contribution)
    {
        if (!_suggestedContributions.Any(existing =>
                existing.Kind == contribution.Kind && existing.Key == contribution.Key))
        {
            _suggestedContributions.Add(contribution);
        }
    }
    public GDClassDeclaration? Class => Script.Class;
    public GDSemanticModel? SemanticModel => Script.SemanticModel;
}
