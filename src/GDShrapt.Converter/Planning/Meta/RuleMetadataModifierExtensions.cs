namespace GDShrapt.Converter.Planning.Meta;

public static class RuleMetadataModifierExtensions
{
    extension(IGDConversionRule rule)
    {
        public IGDConversionRule ReadOnly() => rule.With(ModifierMetadata.ReadOnly);
    }
}
