using System.Diagnostics;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;

namespace GDShrapt.Converter.Planning;

/// <summary>
/// MSBuild-evaluated properties for a C# project under one build configuration.
/// </summary>
public sealed class DefaultCsProjectContext : ICsProjectContext
{
    private const string DoublePrecisionDefine = "GODOT_REAL_T_IS_DOUBLE";

    public DefaultCsProjectContext(string projectFilePath, string configuration = "Debug")
        : this(Evaluate(projectFilePath, configuration))
    {
    }

    private DefaultCsProjectContext(ProjectEvaluation evaluation)
        : this(evaluation.ProjectFilePath, evaluation.Configuration, evaluation.Properties, evaluation.CompileFiles)
    {
    }

    private DefaultCsProjectContext(
        string? projectFilePath,
        string configuration,
        IReadOnlyDictionary<string, string> properties,
        IReadOnlyList<string>? compileFiles = null)
    {
        ProjectFilePath = projectFilePath;
        Configuration = configuration;
        EvaluatedProperties = properties.ToDictionary(
            property => property.Key,
            property => property.Value,
            StringComparer.Ordinal);
        AssemblyName = GetProperty("AssemblyName", projectFilePath == null ? "GodotProject" : Path.GetFileNameWithoutExtension(projectFilePath));
        RootNamespace = GetProperty("RootNamespace", AssemblyName);
        TargetFramework = GetOptionalProperty("TargetFramework")
            ?? GetOptionalProperty("TargetFrameworks")?.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        LanguageVersion = GetProperty("LangVersion", "default");
        NullableEnabled = IsEnabled(GetProperty("Nullable", "disable"));
        ImplicitUsingsEnabled = IsEnabled(GetProperty("ImplicitUsings", "disable"));
        DefineConstants = GetProperty("DefineConstants", string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
        SupportsDoublePrecision = DefineConstants.Contains(DoublePrecisionDefine) ||
            IsEnabled(GetProperty("GodotRealTIsDouble", "false")) ||
            IsEnabled(GetProperty("UseDoublePrecision", "false"));
        (GlobalUsings, GlobalTypeAliases) = LoadGlobalUsings(
            projectFilePath,
            Configuration,
            TargetFramework,
            DefineConstants,
            compileFiles);

        string GetProperty(string name, string fallback)
            => properties.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : fallback;

        string? GetOptionalProperty(string name)
            => properties.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : null;
    }

    public string? ProjectFilePath { get; }
    public string Configuration { get; }
    public string AssemblyName { get; }
    public string RootNamespace { get; }
    public string? TargetFramework { get; }
    public string LanguageVersion { get; }
    public bool NullableEnabled { get; }
    public bool ImplicitUsingsEnabled { get; }
    public bool SupportsDoublePrecision { get; }
    public IReadOnlySet<string> DefineConstants { get; }
    public IReadOnlySet<string> GlobalUsings { get; }
    public IReadOnlyDictionary<string, string> GlobalTypeAliases { get; }
    public IReadOnlyDictionary<string, string> EvaluatedProperties { get; }

    internal static DefaultCsProjectContext Load(string projectFilePath, string configuration)
        => new(projectFilePath, configuration);

    private static ProjectEvaluation Evaluate(string projectFilePath, string configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);
        var fullPath = Path.GetFullPath(projectFilePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"C# project file not found: {fullPath}", fullPath);

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(fullPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(fullPath);
        startInfo.ArgumentList.Add("-getProperty:AssemblyName,RootNamespace,TargetFramework,TargetFrameworks,LangVersion,Nullable,ImplicitUsings,DefineConstants,GodotRealTIsDouble,UseDoublePrecision,IndentStyle,IndentSize,UseTabs,EndOfLine,NamespaceStyle,PreferFileScopedNamespaces,PrivateFieldNamingStyle");
        startInfo.ArgumentList.Add("-getItem:Compile");
        startInfo.ArgumentList.Add($"-property:Configuration={configuration}");
        startInfo.ArgumentList.Add("-nologo");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start 'dotnet msbuild' to evaluate project properties.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"MSBuild failed to evaluate '{fullPath}' for configuration '{configuration}'.{Environment.NewLine}{stderr}{stdout}");

        using var document = JsonDocument.Parse(stdout);
        if (!document.RootElement.TryGetProperty("Properties", out var propertiesElement))
            throw new InvalidOperationException($"MSBuild returned no evaluated properties for '{fullPath}'.");

        var properties = propertiesElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
        var compileFiles = document.RootElement.TryGetProperty("Items", out var itemsElement) &&
                           itemsElement.TryGetProperty("Compile", out var compileItems)
            ? compileItems.EnumerateArray()
                .Select(item => item.TryGetProperty("FullPath", out var fullPath)
                    ? fullPath.GetString()
                    : item.TryGetProperty("Identity", out var identity) ? identity.GetString() : null)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .ToArray()
            : [];
        return new ProjectEvaluation(fullPath, configuration, properties, compileFiles);
    }

    private static (IReadOnlySet<string> Namespaces, IReadOnlyDictionary<string, string> Aliases) LoadGlobalUsings(
        string? projectFilePath,
        string configuration,
        string? targetFramework,
        IReadOnlySet<string> defineConstants,
        IEnumerable<string>? compileFiles)
    {
        if (projectFilePath == null)
            return (new HashSet<string>(StringComparer.Ordinal), new Dictionary<string, string>(StringComparer.Ordinal));

        var projectDirectory = Path.GetDirectoryName(projectFilePath)!;
        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var sourceFiles = compileFiles?.Select(path => Path.IsPathRooted(path)
                ? path
                : Path.GetFullPath(Path.Combine(projectDirectory, path)))
            ?? EnumerateSourceFiles(projectDirectory);
        var parseOptions = new CSharpParseOptions(preprocessorSymbols: defineConstants);
        foreach (var sourceFile in sourceFiles.Where(File.Exists))
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(sourceFile), parseOptions).GetCompilationUnitRoot();
            foreach (var directive in root.Usings.Where(usingDirective => usingDirective.GlobalKeyword.RawKind != 0))
            {
                if (directive.Alias is { } alias)
                    AddGlobalAlias(aliases, alias.Name.Identifier.ValueText, directive.Name?.ToString() ?? string.Empty);
                else if (directive.StaticKeyword.RawKind == 0 &&
                         directive.Name is { } name)
                    namespaces.Add(name.ToString());
            }
        }

        var generatedUsingsDirectory = Path.Combine(projectDirectory, "obj");
        if (Directory.Exists(generatedUsingsDirectory))
        {
            foreach (var sourceFile in Directory.EnumerateFiles(generatedUsingsDirectory, "GlobalUsings.g.cs", SearchOption.AllDirectories)
                         .Where(path => IsSelectedGeneratedUsings(path, projectDirectory, configuration, targetFramework)))
            {
                var root = CSharpSyntaxTree.ParseText(File.ReadAllText(sourceFile), parseOptions).GetCompilationUnitRoot();
                foreach (var directive in root.Usings)
                {
                    if (directive.Name is { } name)
                        namespaces.Add(name.ToString());
                    if (directive.Alias is { } alias && directive.Name is { } aliasTarget)
                        AddGlobalAlias(aliases, alias.Name.Identifier.ValueText, aliasTarget.ToString());
                }
            }
        }

        return (namespaces, aliases);
    }

    private static IEnumerable<string> EnumerateSourceFiles(string projectDirectory)
    {
        var pending = new Stack<string>();
        pending.Push(projectDirectory);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var sourceFile in Directory.EnumerateFiles(directory, "*.cs", SearchOption.TopDirectoryOnly))
                yield return sourceFile;
            foreach (var subdirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(subdirectory);
                if (name is "bin" or "obj" or ".git" or ".godot" or "node_modules" or "packages")
                    continue;
                pending.Push(subdirectory);
            }
        }
    }

    private static bool IsSelectedGeneratedUsings(
        string path,
        string projectDirectory,
        string configuration,
        string? targetFramework)
    {
        var relative = Path.GetRelativePath(projectDirectory, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var objIndex = Array.FindIndex(relative, segment => segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
        if (objIndex < 0 || relative.Length <= objIndex + 1 ||
            !relative[objIndex + 1].Equals(configuration, StringComparison.OrdinalIgnoreCase))
            return false;
        return targetFramework == null || relative.Length <= objIndex + 2 ||
            relative[objIndex + 2].Equals(targetFramework, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddGlobalAlias(
        IDictionary<string, string> aliases,
        string alias,
        string target)
    {
        if (aliases.TryGetValue(alias, out var previous) &&
            !previous.Equals(target, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Global using alias '{alias}' resolves to both '{previous}' and '{target}' in the selected project.");
        }

        aliases[alias] = target;
    }

    internal static DefaultCsProjectContext CreateGodotFallback(string configuration)
        => new(null, configuration, new Dictionary<string, string>(StringComparer.Ordinal));

    private static bool IsEnabled(string value)
        => value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("enable", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("enabled", StringComparison.OrdinalIgnoreCase);

    private sealed record ProjectEvaluation(
        string ProjectFilePath,
        string Configuration,
        IReadOnlyDictionary<string, string> Properties,
        IReadOnlyList<string> CompileFiles);
}
