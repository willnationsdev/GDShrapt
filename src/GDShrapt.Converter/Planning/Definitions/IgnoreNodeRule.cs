using GDShrapt.Reader;
using System.Diagnostics;

namespace GDShrapt.Converter.Planning.Definitions;

public sealed class IgnoreNodeRule(string? reason = null) : IGDConversionRule
{
    public string Name => "fallback-ignore";
    public int Priority => 0;

    public bool CanConvert(GDConversionNodeContext context) => true;

    public GDConversionNodeResult Convert(GDConversionNodeContext context)
    {
        Debug.Assert(context.SemanticModel is not null);
        if (context.Node is GDVariableDeclaration)
        {
            Debug.Assert(context.Parent is not null);
            Debug.Assert(context.Node == context.Parent.Nodes.First(child => ReferenceEquals(child, context.Node)));
        }

        if (reason is not { Length: > 0 })
            reason = $"No C# syntax is needed for {context.Syntax.TypeName}.";
        return GDConversionNodeResult.Ignored(reason);
    }
}

