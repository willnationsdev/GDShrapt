using Microsoft.CodeAnalysis.CSharp;

namespace GDShrapt.Converter.Planning.Meta;

public static class SyntaxNodeExtensions
{
    extension<T>(T node) where T : CSharpSyntaxNode
    {
        public T ApplyMetadata(GDConversionNodeContext context, params ReadOnlySpan<IGDConversionRuleMetadata?> metadata)
        {
            foreach (var meta in metadata)
            {
                if (meta is IGDSyntaxTransformationRuleMetadata st)
                {
                    node = st.Transform(context, node);
                }
            }
            return node;
        }
    }
}
