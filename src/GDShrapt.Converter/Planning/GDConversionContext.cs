using GDShrapt.Semantics;

namespace GDShrapt.Converter.Planning;

public sealed class GDConversionContext(GDConversionAnalysis analysis, GDScriptFile script)
{
    public GDConversionAnalysis Analysis { get; } = analysis;
    public GDScriptFile Script { get; } = script;
    public GDShrapt.Reader.GDClassDeclaration? Class => Script.Class;
    public GDSemanticModel? SemanticModel => Script.SemanticModel;
}
