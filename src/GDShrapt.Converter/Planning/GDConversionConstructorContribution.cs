using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GDShrapt.Converter.Planning;

/// <summary>
/// Adds one injected constructor parameter and its field assignment to the containing type.
/// </summary>
public sealed record GDConversionConstructorContribution(
    ParameterSyntax? Parameter,
    StatementSyntax Assignment) : IGDConversionContribution
{
    public string Kind => "constructor-injection";
    public string Key => Parameter?.Identifier.ValueText ?? Assignment.ToString();
}
