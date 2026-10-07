using GDShrapt.Converter.Planning;
using GDShrapt.Reader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GDShrapt.Converter;

/// <summary>
/// Applies configured rules to every syntax element in analyzed scripts.
/// </summary>
public sealed class GDConversionService
{
    /// <summary>
    /// Applies the highest-priority matching rule to each AST node and terminal token.
    /// Unmatched syntax elements are retained as explicit unmapped entries in the returned plan.
    /// </summary>
    public GDConversionPlan CreatePlan(GDConversionAnalysis analysis, GDConversionRuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(rules);

        var entries = new List<GDConversionPlanEntry>(analysis.Scripts.Count);
        foreach (var script in analysis.Scripts)
        {
            var root = script.Class;
            if (root == null)
            {
                entries.Add(new GDConversionPlanEntry(
                    GetSourcePath(script),
                    GetDefaultOutputPath(script, analysis.Project.ProjectPath),
                    hasSyntaxTree: false,
                    Array.Empty<GDConversionNodeMapping>()));
                continue;
            }

            var conversionContext = new GDConversionContext(analysis, script);
            var mappings = new List<GDConversionNodeMapping>();

            GDConversionNodeMapping MapSyntax(GDSyntaxToken syntax)
            {
                var children = syntax is GDNode node
                    ? node.Tokens.OfType<GDSyntaxToken>().Select(MapSyntax).ToArray()
                    : [];
                var nodeContext = new GDConversionNodeContext(conversionContext, syntax, children);
                var matchingRules = rules.Rules.Where(rule => rule.CanConvert(nodeContext)).ToArray();
                var selectedRule = matchingRules.FirstOrDefault();
                var result = selectedRule == null
                    ? GDConversionNodeResult.Unmapped
                    : selectedRule.Convert(nodeContext) ??
                      throw new InvalidOperationException($"Conversion rule '{selectedRule.Name}' returned a null result for {syntax.GetType().Name}.");

                var mapping = new GDConversionNodeMapping(
                    syntax,
                    syntax.Parent as GDNode,
                    selectedRule?.Name,
                    matchingRules.Select(rule => rule.Name).ToArray(),
                    result);
                mappings.Add(mapping);
                return mapping;
            }

            MapSyntax(root);
            entries.Add(new GDConversionPlanEntry(
                GetSourcePath(script),
                GetDefaultOutputPath(script, analysis.Project.ProjectPath),
                hasSyntaxTree: true,
                ComposeMappings(mappings)));
        }

        var projectRoot = Path.GetFullPath(analysis.Project.ProjectPath);
        return new GDConversionPlan(entries, projectRoot, FindSolutionRoot(projectRoot));
    }

    /// <summary>
    /// Writes the file-producing type declarations in a complete plan under the specified output directory.
    /// </summary>
    public void WriteOutputs(GDConversionPlan plan, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        if (!plan.IsComplete)
            throw new InvalidOperationException($"Cannot write an incomplete conversion plan; {plan.UnmappedNodes.Count()} syntax elements are unmapped.");

        var basePath = plan.ProjectRoot ?? Environment.CurrentDirectory;
        var rootPath = Path.GetFullPath(outputDirectory, basePath);
        if (plan.SolutionRoot is { } solutionRoot && !IsSameOrChildPath(solutionRoot, rootPath))
            throw new InvalidOperationException($"Conversion output directory is outside the solution: {outputDirectory}");
        if (plan.SolutionRoot == null &&
            plan.ProjectRoot is { } projectRoot &&
            !IsSameOrChildPath(projectRoot, rootPath))
        {
            throw new InvalidOperationException($"Conversion output directory is outside the Godot project: {outputDirectory}");
        }

        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var outputPaths = new HashSet<string>(pathComparer);
        var pendingWrites = new List<(string Path, string Content)>(plan.Files.Count);
        foreach (var file in plan.Files)
        {
            if (Path.IsPathRooted(file.OutputPath))
                throw new InvalidOperationException($"Conversion output must be relative to the output directory: {file.OutputPath}");

            var outputPath = Path.GetFullPath(Path.Combine(rootPath, file.OutputPath));
            var isWithinOutputDirectory = IsSameOrChildPath(rootPath, outputPath);
            var isWithinSolution = plan.SolutionRoot is { } root && IsSameOrChildPath(root, outputPath);
            var isWithinProject = plan.ProjectRoot is { } project && IsSameOrChildPath(project, outputPath);
            var isAllowed = plan.SolutionRoot == null
                ? isWithinOutputDirectory && (plan.ProjectRoot == null || isWithinProject)
                : isWithinSolution && (isWithinOutputDirectory || !isWithinProject);
            if (!isAllowed)
                throw new InvalidOperationException($"Conversion output is outside the permitted output locations: {file.OutputPath}");

            if (!outputPaths.Add(outputPath))
                throw new InvalidOperationException($"Multiple output files target the same path: {file.OutputPath}");

            var compilationUnit = CreateCompilationUnit(file);
            var content = compilationUnit.NormalizeWhitespace().ToFullString() + Environment.NewLine;
            pendingWrites.Add((outputPath, content));
        }

        foreach (var (outputPath, content) in pendingWrites)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, content);
        }
    }

    private static string? FindSolutionRoot(string projectRoot)
    {
        for (var directory = new DirectoryInfo(projectRoot); directory != null; directory = directory.Parent)
        {
            if (directory.EnumerateFiles("*.sln", SearchOption.TopDirectoryOnly).Any() ||
                directory.EnumerateFiles("*.slnx", SearchOption.TopDirectoryOnly).Any())
                return directory.FullName;
        }

        return null;
    }

    private static bool IsSameOrChildPath(string rootPath, string path)
    {
        var relativePath = Path.GetRelativePath(rootPath, path);
        return relativePath == "." ||
               (relativePath != ".." &&
                !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !Path.IsPathRooted(relativePath));
    }

    private static CompilationUnitSyntax CreateCompilationUnit(GDConversionPlannedFile file)
    {
        var compilationUnit = SyntaxFactory.CompilationUnit();
        foreach (var type in file.Types)
        {
            var fullyQualifiedName = type.Destination.FullName;
            var separator = fullyQualifiedName.LastIndexOf('.');
            if (separator < 0)
            {
                compilationUnit = compilationUnit.AddMembers(type.Declaration);
                continue;
            }

            var namespaceName = fullyQualifiedName[..separator];
            var namespaceDeclaration = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(namespaceName))
                .AddMembers(type.Declaration);
            compilationUnit = compilationUnit.AddMembers(namespaceDeclaration);
        }

        return compilationUnit;
    }

    private static IReadOnlyList<GDConversionNodeMapping> ComposeMappings(
        IReadOnlyList<GDConversionNodeMapping> mappings)
    {
        var mappingsBySyntax = mappings.ToDictionary(
            mapping => mapping.Syntax,
            new GDReferenceEqualityComparer<GDSyntaxToken>());
        var composed = new Dictionary<GDSyntaxToken, GDConversionNodeMapping>(
            new GDReferenceEqualityComparer<GDSyntaxToken>());

        GDConversionNodeMapping Compose(GDConversionNodeMapping mapping)
        {
            if (composed.TryGetValue(mapping.Syntax, out var existing))
                return existing;

            var result = mapping.Result;
            if (mapping.Node is GDClassDeclaration classDeclaration &&
                result.CSharpSyntax is TypeDeclarationSyntax classSyntax)
            {
                var sourceMembers = classDeclaration.Nodes.OfType<GDClassMembersList>().FirstOrDefault();
                var members = ((IEnumerable<GDClassMember>?)sourceMembers ?? [])
                    .Select((member, index) => (Mapping: Compose(mappingsBySyntax[member]), Index: index))
                    .Where(item => item.Mapping.Result.Destination == null)
                    .OrderBy(item => item.Index)
                    .Select(item => item.Mapping.Result.CSharpSyntax)
                    .OfType<MemberDeclarationSyntax>();
                result = result.WithCSharpSyntax(classSyntax.WithMembers(SyntaxFactory.List(members)));
            }
            else if (mapping.Node is GDInnerClassDeclaration innerClassDeclaration &&
                result.CSharpSyntax is TypeDeclarationSyntax innerClassSyntax)
            {
                var sourceMembers = innerClassDeclaration.Nodes.OfType<GDClassMembersList>().FirstOrDefault();
                var members = ((IEnumerable<GDClassMember>?)sourceMembers ?? [])
                    .Select((member, index) => (Mapping: Compose(mappingsBySyntax[member]), Index: index))
                    .Where(item => item.Mapping.Result.Destination == null)
                    .OrderBy(item => item.Index)
                    .Select(item => item.Mapping.Result.CSharpSyntax)
                    .OfType<MemberDeclarationSyntax>();
                result = result.WithCSharpSyntax(innerClassSyntax.WithMembers(SyntaxFactory.List(members)));
            }
            else if (mapping.Node is GDEnumDeclaration enumDeclaration &&
                result.CSharpSyntax is EnumDeclarationSyntax enumSyntax)
            {
                var sourceValues = enumDeclaration.Nodes.OfType<GDEnumValuesList>().FirstOrDefault();
                var values = ((IEnumerable<GDEnumValueDeclaration>?)sourceValues ?? [])
                    .Select((value, index) => (Mapping: Compose(mappingsBySyntax[value]), Index: index))
                    .Where(item => item.Mapping.Result.Destination == null)
                    .OrderBy(item => item.Index)
                    .Select(item => item.Mapping.Result.CSharpSyntax)
                    .OfType<EnumMemberDeclarationSyntax>();
                result = result.WithCSharpSyntax(enumSyntax.WithMembers(SyntaxFactory.SeparatedList(values)));
            }
            else if (mapping.Node is GDMethodDeclaration methodDeclaration &&
                result.CSharpSyntax is MethodDeclarationSyntax methodSyntax &&
                methodDeclaration.Nodes.OfType<GDStatementsList>().FirstOrDefault() is { } sourceStatements &&
                mappingsBySyntax.TryGetValue(sourceStatements, out var statementsMapping))
            {
                var statements = Compose(statementsMapping).Result.CSharpSyntax as BlockSyntax;
                if (statements != null)
                    result = result.WithCSharpSyntax(methodSyntax.WithBody(statements).WithExpressionBody(null).WithSemicolonToken(default));
            }
            else if (mapping.Node is GDStatementsList statementList &&
                result.CSharpSyntax is BlockSyntax blockSyntax)
            {
                var statements = statementList
                    .Select(statement => Compose(mappingsBySyntax[statement]).Result.CSharpSyntax)
                    .OfType<StatementSyntax>();
                result = result.WithCSharpSyntax(blockSyntax.WithStatements(SyntaxFactory.List(statements)));
            }

            var composedMapping = mapping with { Result = result };
            composed.Add(mapping.Syntax, composedMapping);
            return composedMapping;
        }

        return mappings.Select(Compose).ToArray();
    }

    private static string GetSourcePath(GDShrapt.Semantics.GDScriptFile script)
        => script.FullPath ?? script.ResPath ?? script.TypeName ?? "<unknown>";

    private static string GetDefaultOutputPath(
        GDShrapt.Semantics.GDScriptFile script,
        string projectRoot)
    {
        if (script.FullPath == null)
            return $"{script.TypeName ?? "Script"}.cs";

        var relativePath = Path.GetRelativePath(projectRoot, script.FullPath);
        if (Path.IsPathRooted(relativePath) ||
            relativePath == ".." ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            relativePath = Path.GetFileName(relativePath);
        }

        return Path.ChangeExtension(relativePath, ".cs");
    }
}
