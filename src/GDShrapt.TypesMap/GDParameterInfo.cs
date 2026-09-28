using Godot;

namespace GDShrapt.TypesMap;

public class GDParameterInfo
{
    public string? CSharpName { get; set; }
    public string? GDScriptTypeName { get; set; }
    public string? CallableReceivesType { get; set; }
    public string? CallableReturnsType { get; set; }
    public int? CallableParameterCount { get; set; }
    public bool IsParams { get; set; }
    public bool HasDefaultValue { get; set; }
    //public Variant DefaultValue { get; set; }
}
