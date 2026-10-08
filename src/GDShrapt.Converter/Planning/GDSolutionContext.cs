namespace GDShrapt.Converter.Planning;

/// <summary>
/// Loads evaluated C# project data and formatting settings for the Godot project and
/// C# projects found under its enclosing solution.
/// </summary>
public sealed class GDSolutionContext : ISolutionContext
{
    private readonly object _sync = new();
    private readonly string[] _projectFiles;
    private readonly Dictionary<string, string> _assemblyNamesByAlias = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ICsProjectContext> _projectsByAssembly = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IGDConversionFormatter> _formattersByAssembly = new(StringComparer.OrdinalIgnoreCase);

    public const string DefaultExtensionsAssemblyName = "Godot.Extensions";
    public const string DefaultToolsAssemblyName = "Godot.Extensions.Tools";

    public GDSolutionContext(
        string projectPath,
        string configuration = "Debug",
        IEnumerable<string>? assemblies = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);
        RegisterAssemblies(assemblies);

        var fullPath = Path.GetFullPath(projectPath);
        var projectDirectory = File.Exists(fullPath) ? Path.GetDirectoryName(fullPath)! : fullPath;
        if (!Directory.Exists(projectDirectory))
            throw new DirectoryNotFoundException($"Project directory not found: {projectDirectory}");

        GodotProjectRoot = projectDirectory;
        SolutionRoot = FileSystemHelper.FindSolutionRoot(projectDirectory) ?? projectDirectory;
        Configuration = configuration;
        _projectFiles = FindProjectFiles(SolutionRoot).ToArray();

        var defaultProjectFile = File.Exists(fullPath) && Path.GetExtension(fullPath).Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : SelectDefaultProject(_projectFiles, projectDirectory);
        if (defaultProjectFile != null && !_projectFiles.Contains(defaultProjectFile, PathComparer))
            _projectFiles = _projectFiles.Append(Path.GetFullPath(defaultProjectFile)).ToArray();

        if (defaultProjectFile == null)
        {
            DefaultProject = DefaultCsProjectContext.CreateGodotFallback(configuration);
            DefaultFormatter = CreateFormatter(projectDirectory, DefaultProject);
        }
        else
        {
            DefaultProject = LoadProject(defaultProjectFile);
            DefaultFormatter = CreateFormatter(Path.GetDirectoryName(defaultProjectFile)!, DefaultProject);
        }
    }

    public string GodotProjectRoot { get; }
    public string SolutionRoot { get; }
    public string Configuration { get; }
    public IGDConversionFormatter DefaultFormatter { get; }
    public ICsProjectContext DefaultProject { get; }

    public IGDConversionFormatter Formatter(string assemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        lock (_sync)
        {
            var resolvedAssemblyName = ResolveAssemblyName(assemblyName);
            if (_formattersByAssembly.TryGetValue(resolvedAssemblyName, out var formatter))
                return formatter;

            var project = ResolveProject(resolvedAssemblyName);
            formatter = CreateFormatter(Path.GetDirectoryName(project.ProjectFilePath!)!, project);
            _formattersByAssembly.Add(project.AssemblyName, formatter);
            return formatter;
        }
    }

    public ICsProjectContext Project(string assemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        lock (_sync)
            return ResolveProject(assemblyName);
    }

    private ICsProjectContext ResolveProject(string assemblyName)
    {
        var resolvedAssemblyName = ResolveAssemblyName(assemblyName);
        if (_projectsByAssembly.TryGetValue(resolvedAssemblyName, out var knownProject))
            return knownProject;

        foreach (var projectFile in _projectFiles)
        {
            if (_projectsByAssembly.Values.Any(project =>
                    PathComparer.Equals(project.ProjectFilePath, projectFile)))
            {
                continue;
            }

            var project = LoadProject(projectFile);
            if (string.Equals(project.AssemblyName, resolvedAssemblyName, StringComparison.OrdinalIgnoreCase))
                return project;
        }

        var registration = string.Equals(assemblyName, resolvedAssemblyName, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : $" (registered by '{assemblyName}')";
        throw new KeyNotFoundException(
            $"No C# project named '{resolvedAssemblyName}'{registration} was found in '{SolutionRoot}'.");
    }

    private void RegisterAssemblies(IEnumerable<string>? assemblies)
    {
        if (assemblies == null)
            return;

        foreach (var registration in assemblies)
        {
            if (string.IsNullOrWhiteSpace(registration))
                throw new ArgumentException("Assembly registrations cannot be empty.", nameof(assemblies));

            var value = registration.Trim();
            var separatorIndex = value.IndexOf(':');
            string alias;
            string assemblyName;
            if (separatorIndex < 0)
            {
                alias = value;
                assemblyName = value;
            }
            else
            {
                if (separatorIndex == 0 ||
                    separatorIndex == value.Length - 1 ||
                    separatorIndex != value.LastIndexOf(':'))
                {
                    throw new ArgumentException(
                        $"Invalid assembly registration '{registration}'. Expected 'alias:assembly-name' or 'assembly-name'.",
                        nameof(assemblies));
                }

                alias = value[..separatorIndex].Trim();
                assemblyName = value[(separatorIndex + 1)..].Trim();
                if (alias.Length == 0 || assemblyName.Length == 0)
                {
                    throw new ArgumentException(
                        $"Invalid assembly registration '{registration}'. Expected 'alias:assembly-name' or 'assembly-name'.",
                        nameof(assemblies));
                }
            }

            if (_assemblyNamesByAlias.TryGetValue(alias, out var existingAssemblyName))
            {
                if (!string.Equals(existingAssemblyName, assemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        $"Assembly alias '{alias}' is registered for both '{existingAssemblyName}' and '{assemblyName}'.",
                        nameof(assemblies));
                }

                continue;
            }

            _assemblyNamesByAlias.Add(alias, assemblyName);
        }
    }

    private string ResolveAssemblyName(string assemblyName)
        => _assemblyNamesByAlias.TryGetValue(assemblyName, out var resolvedAssemblyName)
            ? resolvedAssemblyName
            : assemblyName;

    private ICsProjectContext LoadProject(string projectFile)
    {
        var loaded = DefaultCsProjectContext.Load(projectFile, Configuration);
        if (_projectsByAssembly.TryGetValue(loaded.AssemblyName, out var existing) &&
            !PathComparer.Equals(existing.ProjectFilePath, loaded.ProjectFilePath))
        {
            throw new InvalidOperationException(
                $"More than one C# project in '{SolutionRoot}' produces assembly '{loaded.AssemblyName}'.");
        }

        _projectsByAssembly[loaded.AssemblyName] = loaded;
        return loaded;
    }

    private IGDConversionFormatter CreateFormatter(string projectDirectory, ICsProjectContext project)
    {
        if (project.ProjectFilePath != null &&
            _formattersByAssembly.TryGetValue(project.AssemblyName, out var cached))
        {
            return cached;
        }

        return new DefaultGDConversionFormatter(
            GDConversionSettingsLoader.Load(projectDirectory, SolutionRoot, project));
    }

    private static string? SelectDefaultProject(IReadOnlyList<string> projectFiles, string godotProjectRoot)
    {
        var containingProject = projectFiles
            .Where(path => FileSystemHelper.IsSameOrChildPath(
                Path.GetDirectoryName(path)!,
                godotProjectRoot))
            .OrderByDescending(path => Path.GetDirectoryName(path)!.Length)
            .FirstOrDefault();
        if (containingProject != null)
            return containingProject;

        return projectFiles
            .Where(path => FileSystemHelper.IsSameOrChildPath(
                godotProjectRoot,
                Path.GetDirectoryName(path)!))
            .OrderBy(path => Path.GetRelativePath(godotProjectRoot, path).Length)
            .FirstOrDefault();
    }

    private static IEnumerable<string> FindProjectFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var projectFile in Directory.EnumerateFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly))
                yield return Path.GetFullPath(projectFile);
            foreach (var subdirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(subdirectory);
                if (name is "bin" or "obj" or ".git" or ".godot" or "node_modules" or "packages")
                    continue;
                pending.Push(subdirectory);
            }
        }
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
