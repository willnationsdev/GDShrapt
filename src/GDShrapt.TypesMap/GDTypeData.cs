using Godot;
using System.Collections.Generic;

namespace GDShrapt.TypesMap;

public record EnumBuiltInMethodCollection(StringName Name, List<GDMethodData> Methods);

public class GDTypeData
{
    public string GDScriptName { get; set; } = "";
    public string? GDScriptBaseTypeName { get; set; }
    public string? CSharpNamespace { get; set; }
    public string? CSharpName { get; set; }
    public bool IsEnum { get; set; }
    public bool IsBuiltin { get; set; }
    public Dictionary<string, List<GDMethodData>>? MethodDatas { get; set; }
    public Dictionary<string, GDConstantData>? Constants { get; set; }
    public Dictionary<string, GDPropertyData>? PropertyDatas { get; set; }
    public Dictionary<string, GDSignalData>? SignalDatas { get; set; }
    public Dictionary<string, GDEnumData>? Enums { get; set; }
    public GDTypeTraits? Traits { get; set; }
    public GDTypeOperatorsCollection? Operators { get; set; }
    public string? Description { get; set; }
    public string? BriefDescription { get; set; }

    /// <summary>
    /// Manually reconstructs the built-in methods associated with enums.
    /// values(), keys(), size(), has(), find_key()
    /// </summary>
    /// <returns></returns>
    public static IEnumerable<EnumBuiltInMethodCollection> CreateEnumBuiltinMethods()
    {
        var enums = new List<string>(); // TODO: acquire from somewhere
        foreach (var @enum in enums)
        {
            var col = new EnumBuiltInMethodCollection(@enum, []);
            // TODO: populate method objects.
            yield return col;
        }
    }
}
