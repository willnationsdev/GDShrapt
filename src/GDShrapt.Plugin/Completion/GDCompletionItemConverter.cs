using Core = GDShrapt.CLI.Core;

namespace GDShrapt.Plugin;

/// <summary>
/// Converts CLI.Core completion results (from <see cref="Core.IGDCompletionHandler"/>) into the
/// Plugin-internal <see cref="GDCompletionItem"/> consumed by the completion popup, and maps the
/// Plugin completion-type to the handler's request type. The handler is the single source of truth
/// for completion content; the Plugin only renders it.
/// </summary>
internal static class GDCompletionItemConverter
{
    public static GDCompletionItem ToPluginItem(Core.GDCompletionItem item) => new()
    {
        Label = item.Label,
        InsertText = string.IsNullOrEmpty(item.InsertText) ? item.Label : item.InsertText!,
        Detail = item.Detail,
        Documentation = item.Documentation,
        Kind = ToPluginKind(item.Kind),
        TypeName = item.Detail,
        SortPriority = item.SortPriority,
        IsSnippet = item.IsSnippet || item.Kind == Core.GDCompletionItemKind.Snippet,
        Source = ToPluginSource(item.Source),
    };

    public static Core.GDCompletionType ToCoreType(GDCompletionType type) => type switch
    {
        GDCompletionType.MemberAccess => Core.GDCompletionType.MemberAccess,
        GDCompletionType.TypeAnnotation => Core.GDCompletionType.TypeAnnotation,
        GDCompletionType.NodePath => Core.GDCompletionType.NodePath,
        _ => Core.GDCompletionType.Symbol,
    };

    private static GDCompletionItemKind ToPluginKind(Core.GDCompletionItemKind kind) => kind switch
    {
        Core.GDCompletionItemKind.Method => GDCompletionItemKind.Method,
        Core.GDCompletionItemKind.Function => GDCompletionItemKind.Function,
        Core.GDCompletionItemKind.Variable => GDCompletionItemKind.Variable,
        Core.GDCompletionItemKind.Field => GDCompletionItemKind.Field,
        Core.GDCompletionItemKind.Property => GDCompletionItemKind.Property,
        Core.GDCompletionItemKind.Class => GDCompletionItemKind.Class,
        Core.GDCompletionItemKind.Interface => GDCompletionItemKind.Interface,
        Core.GDCompletionItemKind.Struct => GDCompletionItemKind.Class,
        Core.GDCompletionItemKind.Enum => GDCompletionItemKind.Enum,
        Core.GDCompletionItemKind.EnumMember => GDCompletionItemKind.EnumMember,
        Core.GDCompletionItemKind.Constant => GDCompletionItemKind.Constant,
        Core.GDCompletionItemKind.Event => GDCompletionItemKind.Event,
        Core.GDCompletionItemKind.Keyword => GDCompletionItemKind.Keyword,
        Core.GDCompletionItemKind.Snippet => GDCompletionItemKind.Snippet,
        _ => GDCompletionItemKind.Text,
    };

    private static GDCompletionSource ToPluginSource(Core.GDCompletionSource source) => source switch
    {
        Core.GDCompletionSource.BuiltIn => GDCompletionSource.BuiltIn,
        Core.GDCompletionSource.GodotApi => GDCompletionSource.GodotApi,
        Core.GDCompletionSource.Script => GDCompletionSource.Script,
        Core.GDCompletionSource.Project => GDCompletionSource.Project,
        Core.GDCompletionSource.Local => GDCompletionSource.Local,
        _ => GDCompletionSource.Unknown,
    };
}
