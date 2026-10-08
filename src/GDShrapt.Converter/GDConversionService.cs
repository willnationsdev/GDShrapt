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
    private readonly ISolutionContext _solution;
    private readonly IGDConversionTypeRepresentationPolicy? _typeRepresentationPolicy;

    public GDConversionService(
        ISolutionContext solution,
        IGDConversionTypeRepresentationPolicy? typeRepresentationPolicy = null)
    {
        _solution = solution;
        _typeRepresentationPolicy = typeRepresentationPolicy;
    }

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
                    []));
                continue;
            }

            var conversionContext = new GDConversionContext(analysis, script, _solution, _typeRepresentationPolicy);
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
                    syntax.Parent,
                    selectedRule?.Name,
                    matchingRules.Select(rule => rule.Name).ToArray(),
                    result);
                if (result.Disposition == GDConversionNodeDisposition.Converted && nodeContext.Contributions.Count > 0)
                {
                    mapping = mapping with
                    {
                        Result = result.WithContributions(result.Contributions.Concat(nodeContext.Contributions)
                            .DistinctBy(contribution => (contribution.Kind, contribution.Key)))
                    };
                }
                mappings.Add(mapping);
                return mapping;
            }

            MapSyntax(root);
            if (conversionContext.SuggestedContributions.Count > 0)
            {
                var rootMappingIndex = mappings.FindLastIndex(mapping => ReferenceEquals(mapping.Syntax, root));
                var rootMapping = mappings[rootMappingIndex];
                mappings[rootMappingIndex] = rootMapping with
                {
                    Result = rootMapping.Result.WithContributions(
                        rootMapping.Result.Contributions.Concat(conversionContext.SuggestedContributions)
                            .DistinctBy(contribution => (contribution.Kind, contribution.Key)))
                };
            }
            entries.Add(new GDConversionPlanEntry(
                GetSourcePath(script),
                GetDefaultOutputPath(script, analysis.Project.ProjectPath),
                hasSyntaxTree: true,
                ComposeMappings(mappings)));
        }

        var projectRoot = Path.GetFullPath(analysis.Project.ProjectPath);
        return new GDConversionPlan(entries, projectRoot, FileSystemHelper.FindSolutionRoot(projectRoot));
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
        if (plan.SolutionRoot is { } solutionRoot && !FileSystemHelper.IsSameOrChildPath(solutionRoot, rootPath))
            throw new InvalidOperationException($"Conversion output directory is outside the solution: {outputDirectory}");
        if (plan.SolutionRoot == null &&
            plan.ProjectRoot is { } projectRoot &&
            !FileSystemHelper.IsSameOrChildPath(projectRoot, rootPath))
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
            var isWithinOutputDirectory = FileSystemHelper.IsSameOrChildPath(rootPath, outputPath);
            var isWithinSolution = plan.SolutionRoot is { } root && FileSystemHelper.IsSameOrChildPath(root, outputPath);
            var isWithinProject = plan.ProjectRoot is { } project && FileSystemHelper.IsSameOrChildPath(project, outputPath);
            var isAllowed = plan.SolutionRoot == null
                ? isWithinOutputDirectory && (plan.ProjectRoot == null || isWithinProject)
                : isWithinSolution && (isWithinOutputDirectory || !isWithinProject);
            if (!isAllowed)
                throw new InvalidOperationException($"Conversion output is outside the permitted output locations: {file.OutputPath}");

            if (!outputPaths.Add(outputPath))
                throw new InvalidOperationException($"Multiple output files target the same path: {file.OutputPath}");

            var formatter = file.AssemblyName == null
                ? _solution.DefaultFormatter
                : _solution.Formatter(file.AssemblyName);
            var indentation = formatter.Options.UseTabIndent
                ? "\t"
                : new string(' ', Math.Max(1, formatter.Options.IndentSize));
            var csProject = _solution.Project(file.AssemblyName ?? _solution.DefaultProject.AssemblyName);
            var (resolvedFile, usings) = ResolveFileContributions(file, csProject);
            var lineEnding = formatter.Options.GetLineEnding(outputPath);
            var compilationUnit = CreateCompilationUnit(resolvedFile, formatter.Options, usings);
            var content = compilationUnit
                .NormalizeWhitespace(indentation, lineEnding)
                .ToFullString() + lineEnding;
            pendingWrites.Add((outputPath, content));
        }

        foreach (var (outputPath, content) in pendingWrites)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, content);
        }
    }

    private static CompilationUnitSyntax CreateCompilationUnit(
        GDConversionPlannedFile file,
        GDConversionFormattingOptions formattingOptions,
        IReadOnlyList<string> imports)
    {
        var compilationUnit = SyntaxFactory.CompilationUnit()
            .AddUsings(imports.Select(name => SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(name))).ToArray());
        var namespaceTypes = new List<(string Namespace, MemberDeclarationSyntax Declaration)>();
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
            namespaceTypes.Add((namespaceName, type.Declaration));
        }

        var namespaceNames = namespaceTypes
            .Select(item => item.Namespace)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var hasUnnamespacedTypes = file.Types.Any(type => !type.Destination.FullName.Contains('.'));
        if (formattingOptions.PreferFileScopedNamespaces &&
            namespaceNames.Length == 1 &&
            !hasUnnamespacedTypes)
        {
            var fileScopedNamespace = SyntaxFactory.FileScopedNamespaceDeclaration(
                    SyntaxFactory.ParseName(namespaceNames[0]))
                .AddMembers(namespaceTypes.Select(item => item.Declaration).ToArray());
            return compilationUnit.AddMembers(fileScopedNamespace);
        }

        foreach (var (namespaceName, declaration) in namespaceTypes)
        {
            var namespaceDeclaration = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(namespaceName))
                .AddMembers(declaration);
            compilationUnit = compilationUnit.AddMembers(namespaceDeclaration);
        }

        return compilationUnit;
    }

    private static (GDConversionPlannedFile File, IReadOnlyList<string> Usings) ResolveFileContributions(
        GDConversionPlannedFile file,
        ICsProjectContext project)
    {
        var usingContributions = file.Types
            .SelectMany(type => type.Contributions)
            .SelectMany(mapping => mapping.Result.Contributions)
            .OfType<GDConversionNamespaceUsingContribution>()
            .DistinctBy(contribution => contribution.Namespace)
            .ToArray();
        if (usingContributions.Length == 0)
            return (file, []);

        var namespaceByTypeName = usingContributions
            .SelectMany(contribution => file.Types.SelectMany(type => type.Declaration.DescendantNodesAndSelf()
                .OfType<NameSyntax>()
                .Where(name => name.ToString().StartsWith(contribution.Namespace + ".", StringComparison.Ordinal))
                .Select(name => (
                    Namespace: contribution.Namespace,
                    TypeName: name.ToString()[(contribution.Namespace.Length + 1)..].Split('<', '.')[0]))))
            .GroupBy(item => item.TypeName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Namespace).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        var adopted = usingContributions.ToDictionary(
            contribution => contribution.Namespace,
            contribution => namespaceByTypeName
                .Where(item => item.Value.Contains(contribution.Namespace, StringComparer.Ordinal))
                .All(item => item.Value.Length == 1 &&
                    !project.GlobalTypeAliases.ContainsKey(item.Key) &&
                    !file.Types.Any(type =>
                        type.Declaration.Identifier.ValueText.Equals(item.Key, StringComparison.Ordinal))),
            StringComparer.Ordinal);

        var resolvedTypes = file.Types.Select(type =>
        {
            var declaration = type.Declaration;
            foreach (var contribution in usingContributions)
            {
                declaration = (BaseTypeDeclarationSyntax)contribution.ResolveSyntax(
                    declaration,
                    adopted[contribution.Namespace]);
            }
            return type with { Declaration = declaration };
        }).ToArray();
        var imports = usingContributions
            .Where(contribution => adopted[contribution.Namespace] &&
                !project.GlobalUsings.Contains(contribution.Namespace))
            .Select(contribution => contribution.Namespace)
            .ToArray();
        return (file with { Types = resolvedTypes }, imports);
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
                var classMembers = members.ToArray();
                result = result.WithCSharpSyntax(classSyntax.WithMembers(
                    SyntaxFactory.List(AddContributedConstructor(classSyntax, classMembers, sourceMembers, mappingsBySyntax))));
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
                var classMembers = members.ToArray();
                result = result.WithCSharpSyntax(innerClassSyntax.WithMembers(
                    SyntaxFactory.List(AddContributedConstructor(innerClassSyntax, classMembers, sourceMembers, mappingsBySyntax))));
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

    private static IReadOnlyList<MemberDeclarationSyntax> AddContributedConstructor(
        TypeDeclarationSyntax classSyntax,
        IReadOnlyList<MemberDeclarationSyntax> mappedMembers,
        GDClassMembersList? sourceMembers,
        IReadOnlyDictionary<GDSyntaxToken, GDConversionNodeMapping> mappingsBySyntax)
    {
        if (sourceMembers == null)
            return mappedMembers;

        var contributions = sourceMembers
            .SelectMany(member => mappingsBySyntax[member].Result.Contributions)
            .OfType<GDConversionConstructorContribution>()
            .ToArray();
        if (contributions.Length == 0)
            return mappedMembers;

        var existingConstructors = mappedMembers.OfType<ConstructorDeclarationSyntax>().ToArray();
        if (existingConstructors.Length > 1)
            throw new InvalidOperationException(
                $"Cannot merge generated constructor contributions into '{classSyntax.Identifier.ValueText}' because multiple constructors are already mapped.");
        var existingConstructor = existingConstructors.SingleOrDefault();
        var parameters = new List<ParameterSyntax>(existingConstructor?.ParameterList.Parameters ?? default);
        var assignments = new List<StatementSyntax>(existingConstructor?.Body?.Statements ?? default);
        foreach (var contribution in contributions)
        {
            if (contribution.Parameter is { } newParameter)
            {
                var existingParameter = parameters.FirstOrDefault(parameter =>
                    parameter.Identifier.ValueText == newParameter.Identifier.ValueText);
                if (existingParameter == null)
                    parameters.Add(newParameter);
                else if (existingParameter.Type?.ToString() != newParameter.Type?.ToString())
                    throw new InvalidOperationException(
                        $"Constructor parameter '{newParameter.Identifier.ValueText}' has conflicting types.");
            }

            assignments.Add(contribution.Assignment);
        }

        var constructor = existingConstructor == null
            ? SyntaxFactory.ConstructorDeclaration(classSyntax.Identifier)
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
            : existingConstructor;
        constructor = constructor
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters)))
            .WithBody(SyntaxFactory.Block(assignments))
            .WithExpressionBody(null)
            .WithSemicolonToken(default);

        var result = mappedMembers.ToList();
        if (existingConstructor != null)
            result[result.IndexOf(existingConstructor)] = constructor;
        else
            result.Add(constructor);
        return result;
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
