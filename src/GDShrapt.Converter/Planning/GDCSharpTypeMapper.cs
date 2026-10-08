using GDShrapt.Abstractions;
using GDShrapt.Reader;

namespace GDShrapt.Converter.Planning;

internal static class GDCSharpTypeMapper
{
    private static readonly HashSet<string> GodotTypes = new(StringComparer.Ordinal)
    {
        "Vector2", "Vector2I", "Vector3", "Vector3I", "Vector4", "Vector4I", "Color", "Rect2", "Rect2I",
        "Transform2D", "Transform3D", "Basis", "Quaternion", "Plane", "AABB", "Projection", "RID", "Callable",
        "Signal", "Object", "RefCounted", "Resource", "Node", "Node2D", "Node3D", "CanvasItem", "Control",
        "Viewport", "Window", "SceneTree", "MainLoop", "Engine", "OS", "Input", "ClassDB", "ResourceLoader",
        "ResourceSaver", "FileAccess", "DirAccess", "PackedScene", "Script", "GDScript", "CSharpScript"
    };

    internal static bool IsGodotTypeName(string typeName) => GodotTypes.Contains(typeName);

    public static string? GetTypeName(
        GDSemanticType? type,
        bool supportsDoublePrecision,
        GDConversionNodeContext context,
        GDNode? useSite = null)
    {
        if (type == null)
            return "Godot.Variant";

        var nativeType = context.GetNativeTypeName(type.DisplayName, type, useSite);
        if (!string.IsNullOrWhiteSpace(nativeType?.TypeName))
            return nativeType.TypeName;

        if (type.IsVariant)
            return ResolveTypeName("Godot", "Variant", context);

        if (type is GDContainerSemanticType container)
        {
            var namespaceName = "Godot.Collections";
            var typeName = container.IsDictionary ? "Dictionary" : "Array";
            var containerName = ResolveTypeName(namespaceName, typeName, context);
            if (container.ElementType.IsVariant ||
                container.IsDictionary && (container.KeyType == null || container.KeyType.IsVariant))
            {
                return containerName;
            }

            var elementType = GetTypeName(container.ElementType, supportsDoublePrecision, context, useSite);
            if (elementType == null)
                return null;

            if (!container.IsDictionary)
                return $"{containerName}<{elementType}>";

            var keyType = GetTypeName(container.KeyType, supportsDoublePrecision, context, useSite);
            return keyType == null ? null : $"{containerName}<{keyType}, {elementType}>";
        }

        return type.DisplayName switch
        {
            "int" => "long",
            "float" => supportsDoublePrecision ? "double" : "float",
            "String" => "string",
            "StringName" => ResolveTypeName("Godot", "StringName", context),
            "NodePath" => ResolveTypeName("Godot", "NodePath", context),
            "bool" => "bool",
            "Variant" => ResolveTypeName("Godot", "Variant", context),
            _ when IsGodotTypeName(type.DisplayName) =>
                ResolveTypeName("Godot", type.DisplayName, context),
            _ => type.DisplayName
        };
    }

    internal static string ResolveTypeName(string namespaceName, string typeName, GDConversionNodeContext context)
    {
        var fullName = $"{namespaceName}.{typeName}";
        var aliases = context.Solution.DefaultProject.GlobalTypeAliases;
        var exactAlias = aliases.FirstOrDefault(alias =>
            NormalizeTypeName(alias.Value).Equals(fullName, StringComparison.Ordinal));
        if (!string.IsNullOrEmpty(exactAlias.Key))
            return exactAlias.Key;

        var namespaceAlias = aliases.FirstOrDefault(alias =>
            NormalizeTypeName(alias.Value).Equals(namespaceName, StringComparison.Ordinal));
        if (!string.IsNullOrEmpty(namespaceAlias.Key))
            return $"{namespaceAlias.Key}.{typeName}";

        if (aliases.ContainsKey(typeName))
            return fullName;

        if (context.Solution.DefaultProject.GlobalUsings.Contains(namespaceName))
            return typeName;

        context.Suggest(new GDConversionNamespaceUsingContribution(namespaceName));
        return fullName;
    }

    private static string NormalizeTypeName(string name)
        => name.StartsWith("global::", StringComparison.Ordinal) ? name["global::".Length..] : name;
}
