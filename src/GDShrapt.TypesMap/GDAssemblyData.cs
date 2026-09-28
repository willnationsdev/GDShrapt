using System.Collections.Generic;

namespace GDShrapt.TypesMap;

public class GDAssemblyData
{
    public Dictionary<string, Dictionary<string, GDTypeData>>? TypeDatas { get; set; }
    public Dictionary<string, GDTypeData>? GlobalTypes { get; set; }
    public Dictionary<string, List<GDEnumInfo>>? Enums { get; set; }
    public Dictionary<string, List<GDMethodData>>? MethodDatas { get; set; }
    public Dictionary<string, List<GDConstantData>>? Constants { get; set; }
    public Dictionary<string, List<GDConstantData>>? EnumsConstants { get; set; }

    public GDAssemblyData? GlobalData { get; }
}

