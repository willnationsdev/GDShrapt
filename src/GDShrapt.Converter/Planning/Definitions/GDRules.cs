namespace GDShrapt.Converter.Planning.Definitions;

public static class Rules
{
    public static IgnoreNodeRule Ignore(string? reason = null) => new IgnoreNodeRule(reason);
}
