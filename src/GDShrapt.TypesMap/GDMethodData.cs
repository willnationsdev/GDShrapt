namespace GDShrapt.TypesMap;

public class GDMethodData
{
    public GDParameterInfo[]? Parameters { get; set; }
    public string GDScriptName { get; set; } = "";
    public string? GDScriptReturnTypeName { get; set; }
    public string? CSharpReturnTypeFullName { get; set; }
    public bool IsVirtual { get; set; }
    public bool IsOverridable { get; set; }
    public string? Description { get; set; }
    public string? ReturnTypeRole { get; set; }
    public string? MergeTypeStrategy { get; set; }
    public int? MinArgs { get; set; }
    public int? MaxArgs { get; set; }
    public bool IsVarArgs { get; set; }
    public bool IsGeneric { get; set; }
    public bool IsStatic { get; set; }
}
