namespace GDShrapt.Converter.Planning;

/// <summary>
/// Evaluated build and project information for one C# project and configuration.
/// </summary>
public interface ICsProjectContext
{
    string? ProjectFilePath { get; }
    string Configuration { get; }
    string AssemblyName { get; }
    string RootNamespace { get; }
    string? TargetFramework { get; }
    string LanguageVersion { get; }
    bool NullableEnabled { get; }
    bool ImplicitUsingsEnabled { get; }
    bool SupportsDoublePrecision { get; }
    IReadOnlySet<string> DefineConstants { get; }
    IReadOnlySet<string> GlobalUsings { get; }
    IReadOnlyDictionary<string, string> GlobalTypeAliases { get; }
    IReadOnlyDictionary<string, string> EvaluatedProperties { get; }
}
