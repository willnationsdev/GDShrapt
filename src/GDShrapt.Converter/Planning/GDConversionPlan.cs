using GDShrapt.Reader;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GDShrapt.Converter.Planning;

/// <summary>
/// The per-syntax-element conversion decision for one syntax tree.
/// </summary>
public sealed record GDConversionNodeMapping(
    GDSyntaxToken Syntax,
    GDNode? Parent,
    string? RuleName,
    IReadOnlyList<string> MatchingRules,
    GDConversionNodeResult Result)
{
    public GDNode? Node => Syntax as GDNode;
}

internal sealed class GDReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
{
    public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
    public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
}

/// <summary>
/// Conversion decisions for one script. A plan entry is complete only when every
/// node and terminal token has been converted or explicitly ignored.
/// </summary>
public sealed class GDConversionPlanEntry(
    string sourcePath,
    string defaultOutputPath,
    bool hasSyntaxTree,
    IReadOnlyList<GDConversionNodeMapping> mappings)
{
    public string SourcePath { get; } = sourcePath;
    public string DefaultOutputPath { get; } = defaultOutputPath;
    public bool HasSyntaxTree { get; } = hasSyntaxTree;
    public IReadOnlyList<GDConversionNodeMapping> Mappings { get; } = mappings;
    public IEnumerable<GDConversionNodeMapping> UnmappedNodes =>
        Mappings.Where(mapping => mapping.Result.Disposition == GDConversionNodeDisposition.Unmapped);
    public bool IsComplete => HasSyntaxTree && !UnmappedNodes.Any();
}

public sealed record GDConversionPlannedType(
    GDConversionDestination Destination,
    BaseTypeDeclarationSyntax Declaration,
    IReadOnlyList<GDConversionNodeMapping> Contributions);

/// <summary>
/// A physical C# file. It can contain multiple destination types when their path
/// overrides resolve to the same output path.
/// </summary>
public sealed record GDConversionPlannedFile(
    string OutputPath,
    string? AssemblyName,
    IReadOnlyList<GDConversionPlannedType> Types);

public sealed class GDConversionPlan
{
    private readonly IReadOnlyList<GDConversionPlanEntry> _entries;

    public GDConversionPlan(
        IReadOnlyList<GDConversionPlanEntry> entries,
        string? projectRoot = null,
        string? solutionRoot = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries;
        ProjectRoot = projectRoot == null ? null : Path.GetFullPath(projectRoot);
        SolutionRoot = solutionRoot == null ? null : Path.GetFullPath(solutionRoot);
        Files = ResolveFiles(entries);
    }

    public IReadOnlyList<GDConversionPlanEntry> Entries => _entries;
    public bool IsComplete => Entries.All(entry => entry.IsComplete);
    public IEnumerable<GDConversionNodeMapping> UnmappedNodes => Entries.SelectMany(entry => entry.UnmappedNodes);
    public IReadOnlyList<GDConversionPlannedFile> Files { get; }
    internal string? ProjectRoot { get; }
    internal string? SolutionRoot { get; }

    private static IReadOnlyList<GDConversionPlannedFile> ResolveFiles(
        IReadOnlyList<GDConversionPlanEntry> entries)
    {
        var mappingsBySyntax = entries
            .SelectMany(entry => entry.Mappings)
            .ToDictionary(mapping => mapping.Syntax, new GDReferenceEqualityComparer<GDSyntaxToken>());
        var destinationGroups = new Dictionary<DestinationKey, List<TypeContribution>>();
        var contributionIndex = 0;

        foreach (var entry in entries)
        {
            foreach (var mapping in entry.Mappings)
            {
                if (mapping.Result.CSharpSyntax is not BaseTypeDeclarationSyntax declaration)
                    continue;

                var destination = mapping.Result.Destination;
                var isScriptClass = mapping.Node is GDClassDeclaration;
                if (destination?.InnerClassName != null || (destination == null && !isScriptClass))
                    continue;

                destination ??= new GDConversionDestination(GetDeclarationName(declaration));
                ValidateDestinationName(destination, declaration);
                var key = DestinationKey.From(destination);
                if (!destinationGroups.TryGetValue(key, out var contributions))
                {
                    contributions = new List<TypeContribution>();
                    destinationGroups.Add(key, contributions);
                }

                contributions.Add(new TypeContribution(
                    entry,
                    mapping,
                    destination,
                    declaration,
                    contributionIndex++));
            }
        }

        var resolvedTypes = new Dictionary<DestinationKey, ResolvedType>();
        foreach (var (key, contributions) in destinationGroups)
            resolvedTypes.Add(key, MergeDestination(contributions, mappingsBySyntax));

        foreach (var entry in entries)
        {
            foreach (var mapping in entry.Mappings)
            {
                var destination = mapping.Result.Destination;
                var syntax = mapping.Result.CSharpSyntax;
                if (destination == null || syntax == null)
                    continue;

                var isFileType = syntax is BaseTypeDeclarationSyntax && destination.InnerClassName == null;
                if (isFileType)
                    continue;

                if (syntax is not MemberDeclarationSyntax member)
                    throw new InvalidOperationException(
                        $"A destination can only be assigned to a C# declaration contribution; {syntax.GetType().Name} is not a member declaration.");

                var key = DestinationKey.From(destination);
                if (!resolvedTypes.TryGetValue(key, out var target))
                    throw new InvalidOperationException(
                        $"No file-producing type declaration was mapped for destination '{destination.FullName}' in assembly '{destination.AssemblyName ?? "<default>"}'.");

                target.AddContribution(destination, member, mapping, contributionIndex++);
            }
        }

        var typesByPath = new Dictionary<FileKey, List<ResolvedType>>(FileKeyComparer.Instance);
        foreach (var resolved in resolvedTypes.Values)
        {
            resolved.Complete();
            var path = ResolveOutputPath(resolved);
            var fileKey = new FileKey(path, resolved.Destination.AssemblyName);
            if (!typesByPath.TryGetValue(fileKey, out var types))
            {
                types = new List<ResolvedType>();
                typesByPath.Add(fileKey, types);
            }
            types.Add(resolved);
        }

        return typesByPath
            .OrderBy(group => group.Value.Min(type => type.FirstContributionIndex))
            .Select(group => new GDConversionPlannedFile(
                group.Key.OutputPath,
                group.Key.AssemblyName,
                group.Value
                    .OrderBy(type => type.Destination.Order)
                    .ThenBy(type => type.FirstContributionIndex)
                    .Select(type => new GDConversionPlannedType(
                        type.Destination,
                        type.Declaration,
                        type.Contributions.OrderBy(item => item.Index).Select(item => item.Mapping).ToArray()))
                    .ToArray()))
            .ToArray();
    }

    private static ResolvedType MergeDestination(
        List<TypeContribution> contributions,
        IReadOnlyDictionary<GDSyntaxToken, GDConversionNodeMapping> mappingsBySyntax)
    {
        var ordered = contributions
            .OrderBy(item => item.Destination.Order)
            .ThenBy(item => item.Index)
            .ToArray();
        var first = ordered[0];
        foreach (var contribution in ordered.Skip(1))
        {
            if (contribution.Declaration.RawKind != first.Declaration.RawKind ||
                !string.Equals(
                    GetDeclarationName(contribution.Declaration),
                    GetDeclarationName(first.Declaration),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Declarations targeting destination '{first.Destination.FullName}' must have the same C# declaration kind and name.");
            }
        }

        var pathOverrides = ordered
            .Select(item => item.Destination.RelativePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => NormalizeRelativePath(path!))
            .Distinct(PathComparer)
            .ToArray();
        if (pathOverrides.Length > 1)
            throw new InvalidOperationException($"Destination '{first.Destination.FullName}' has conflicting relative path overrides.");

        var destination = first.Destination with
        {
            RelativePath = pathOverrides.FirstOrDefault()
        };
        var (declaration, rootMembers) = MergeDeclarations(ordered, mappingsBySyntax);
        var result = new ResolvedType(destination, declaration, rootMembers, first.Entry.DefaultOutputPath, first.Index);
        foreach (var contribution in ordered)
            result.AddRootContribution(contribution.Mapping, contribution.Index);
        return result;
    }

    private static (BaseTypeDeclarationSyntax Declaration, IReadOnlyList<OrderedMember> Members) MergeDeclarations(
        IReadOnlyList<TypeContribution> contributions,
        IReadOnlyDictionary<GDSyntaxToken, GDConversionNodeMapping> mappingsBySyntax)
    {
        var first = contributions[0].Declaration;
        if (first is TypeDeclarationSyntax firstType)
        {
            var members = new List<OrderedMember>();
            foreach (var contribution in contributions)
            {
                if (contribution.Declaration is not TypeDeclarationSyntax type)
                    throw new InvalidOperationException("C# declaration kinds targeting one destination are incompatible.");

                foreach (var member in type.Members)
                {
                    var mapping = FindMemberMapping(contribution.Mapping, member, mappingsBySyntax);
                    members.Add(new OrderedMember(
                        member,
                        mapping?.Result.Destination?.Order ?? contribution.Destination.Order,
                        contribution.Index * 100000 + members.Count));
                }
            }

            var orderedMembers = members.OrderBy(item => item.Order).ThenBy(item => item.Index).ToArray();
            return (firstType.WithMembers(SyntaxFactory.List(orderedMembers.Select(item => item.Member))), orderedMembers);
        }

        if (first is EnumDeclarationSyntax firstEnum)
        {
            var members = new List<OrderedMember>();
            foreach (var contribution in contributions)
            {
                if (contribution.Declaration is not EnumDeclarationSyntax declaration)
                    throw new InvalidOperationException("C# declaration kinds targeting one destination are incompatible.");
                foreach (var member in declaration.Members)
                {
                    members.Add(new OrderedMember(
                        member,
                        contribution.Destination.Order,
                        contribution.Index * 100000 + members.Count));
                }
            }

            var orderedMembers = members.OrderBy(item => item.Order).ThenBy(item => item.Index).ToArray();
            return (firstEnum.WithMembers(SyntaxFactory.SeparatedList(
                orderedMembers.Select(item => (EnumMemberDeclarationSyntax)item.Member))), orderedMembers);
        }

        throw new InvalidOperationException($"C# declaration type '{first.GetType().Name}' cannot be merged.");
    }

    private static GDConversionNodeMapping? FindMemberMapping(
        GDConversionNodeMapping typeMapping,
        MemberDeclarationSyntax member,
        IReadOnlyDictionary<GDSyntaxToken, GDConversionNodeMapping> mappingsBySyntax)
    {
        if (typeMapping.Node is not GDNode typeNode)
            return null;

        return typeNode.AllNodes
            .Select(node => mappingsBySyntax[node])
            .FirstOrDefault(mapping => ReferenceEquals(mapping.Result.CSharpSyntax, member));
    }

    private static string ResolveOutputPath(ResolvedType type)
    {
        var outputPath = type.Destination.RelativePath ?? type.DefaultOutputPath;
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new InvalidOperationException($"No output path could be resolved for destination '{type.Destination.FullName}'.");
        return NormalizeRelativePath(outputPath);
    }

    private static string NormalizeRelativePath(string path)
    {
        if (Path.IsPathRooted(path))
            return path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        var root = Path.GetFullPath("__gdshrapt_conversion_output__");
        var fullPath = Path.GetFullPath(Path.Combine(root, path));
        return Path.GetRelativePath(root, fullPath);
    }

    private static string GetDeclarationName(BaseTypeDeclarationSyntax declaration)
        => declaration switch
        {
            TypeDeclarationSyntax type => type.Identifier.ValueText,
            EnumDeclarationSyntax enumDeclaration => enumDeclaration.Identifier.ValueText,
            _ => throw new InvalidOperationException($"Cannot resolve a destination name for {declaration.GetType().Name}.")
        };

    private static void ValidateDestinationName(
        GDConversionDestination destination,
        BaseTypeDeclarationSyntax declaration)
    {
        var leafName = destination.FullName.Split('.').Last();
        if (!string.Equals(leafName, GetDeclarationName(declaration), StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Destination '{destination.FullName}' does not match C# declaration '{GetDeclarationName(declaration)}'.");
    }

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly record struct DestinationKey(string Name, string? AssemblyName)
    {
        public static DestinationKey From(GDConversionDestination destination)
            => new(destination.FullName, string.IsNullOrWhiteSpace(destination.AssemblyName) ? null : destination.AssemblyName);
    }

    private readonly record struct FileKey(string OutputPath, string? AssemblyName);

    private sealed class FileKeyComparer : IEqualityComparer<FileKey>
    {
        public static FileKeyComparer Instance { get; } = new();

        public bool Equals(FileKey x, FileKey y)
            => PathComparer.Equals(x.OutputPath, y.OutputPath) &&
               string.Equals(x.AssemblyName, y.AssemblyName, StringComparison.Ordinal);

        public int GetHashCode(FileKey obj)
            => HashCode.Combine(PathComparer.GetHashCode(obj.OutputPath), obj.AssemblyName);
    }

    private sealed record TypeContribution(
        GDConversionPlanEntry Entry,
        GDConversionNodeMapping Mapping,
        GDConversionDestination Destination,
        BaseTypeDeclarationSyntax Declaration,
        int Index);

    private sealed record OrderedMember(MemberDeclarationSyntax Member, int Order, int Index);

    private sealed class ResolvedType(
        GDConversionDestination destination,
        BaseTypeDeclarationSyntax declaration,
        IReadOnlyList<OrderedMember> rootMembers,
        string defaultOutputPath,
        int firstContributionIndex)
    {
        private readonly List<(GDConversionDestination Destination, MemberDeclarationSyntax Member, GDConversionNodeMapping Mapping, int Index)> _members = new();
        private readonly List<(GDConversionNodeMapping Mapping, int Index)> _contributions = new();
        private readonly IReadOnlyList<OrderedMember> _rootMembers = rootMembers;
        private string? _relativePathOverride = destination.RelativePath;

        public GDConversionDestination Destination { get; private set; } = destination;
        public BaseTypeDeclarationSyntax Declaration { get; private set; } = declaration;
        public string DefaultOutputPath { get; } = defaultOutputPath;
        public int FirstContributionIndex { get; } = firstContributionIndex;
        public IReadOnlyList<(GDConversionNodeMapping Mapping, int Index)> Contributions => _contributions;

        public void AddRootContribution(GDConversionNodeMapping mapping, int index)
            => _contributions.Add((mapping, index));

        public void AddContribution(
            GDConversionDestination destination,
            MemberDeclarationSyntax member,
            GDConversionNodeMapping mapping,
            int index)
        {
            if (destination.RelativePath is { Length: > 0 } path)
            {
                var normalizedPath = NormalizeRelativePath(path);
                if (_relativePathOverride != null &&
                    !PathComparer.Equals(NormalizeRelativePath(_relativePathOverride), normalizedPath))
                {
                    throw new InvalidOperationException(
                        $"Destination '{Destination.FullName}' has conflicting relative path overrides.");
                }

                _relativePathOverride = normalizedPath;
                Destination = Destination with { RelativePath = normalizedPath };
            }

            _members.Add((destination, member, mapping, index));
            _contributions.Add((mapping, index));
        }

        public void Complete()
        {
            var topLevelMembers = _members
                .Where(item => item.Destination.InnerClassName == null)
                .Select(item => new OrderedMember(item.Member, item.Destination.Order, item.Index))
                .ToArray();
            if (Declaration is TypeDeclarationSyntax type && topLevelMembers.Length > 0)
            {
                var members = _rootMembers.Concat(topLevelMembers)
                    .OrderBy(item => item.Order)
                    .ThenBy(item => item.Index)
                    .Select(item => item.Member);
                Declaration = type.WithMembers(SyntaxFactory.List(members));
            }
            else if (Declaration is EnumDeclarationSyntax enumDeclaration && topLevelMembers.Length > 0)
            {
                var members = _rootMembers.Concat(topLevelMembers)
                    .OrderBy(item => item.Order)
                    .ThenBy(item => item.Index)
                    .Select(item => item.Member)
                    .OfType<EnumMemberDeclarationSyntax>();
                Declaration = enumDeclaration.WithMembers(SyntaxFactory.SeparatedList(members));
            }

            foreach (var contribution in _members
                .Where(item => item.Destination.InnerClassName != null)
                .OrderBy(item => item.Destination.Order)
                .ThenBy(item => item.Index))
            {
                Declaration = AddMembers(Declaration, contribution.Destination.InnerClassName, new[] { contribution.Member });
            }
        }
    }

    private static BaseTypeDeclarationSyntax AddMembers(
        BaseTypeDeclarationSyntax declaration,
        string? innerClassName,
        IEnumerable<MemberDeclarationSyntax> members)
    {
        var orderedMembers = members.ToArray();
        if (innerClassName != null)
            return AddMembersToInnerType(declaration, innerClassName.Split('.'), orderedMembers);

        if (declaration is TypeDeclarationSyntax type)
            return type.WithMembers(type.Members.AddRange(orderedMembers));
        if (declaration is EnumDeclarationSyntax enumDeclaration)
        {
            var enumMembers = orderedMembers.OfType<EnumMemberDeclarationSyntax>().ToArray();
            if (enumMembers.Length != orderedMembers.Length)
                throw new InvalidOperationException("Only enum members can be added to an enum destination.");
            return enumDeclaration.WithMembers(enumDeclaration.Members.AddRange(enumMembers));
        }

        throw new InvalidOperationException($"C# declaration type '{declaration.GetType().Name}' cannot receive members.");
    }

    private static BaseTypeDeclarationSyntax AddMembersToInnerType(
        BaseTypeDeclarationSyntax outer,
        IReadOnlyList<string> path,
        IReadOnlyList<MemberDeclarationSyntax> members)
    {
        if (path.Count == 0 || outer is not TypeDeclarationSyntax outerType)
            throw new InvalidOperationException($"Inner class destination '{string.Join(".", path)}' does not exist.");

        var nestedIndex = -1;
        for (var index = 0; index < outerType.Members.Count; index++)
        {
            if (outerType.Members[index] is TypeDeclarationSyntax nested &&
                string.Equals(nested.Identifier.ValueText, path[0], StringComparison.Ordinal))
            {
                nestedIndex = index;
                break;
            }
        }

        if (nestedIndex < 0)
            throw new InvalidOperationException(
                $"Destination inner class '{path[0]}' was not declared in '{GetDeclarationName(outer)}'.");

        var target = (TypeDeclarationSyntax)outerType.Members[nestedIndex];
        BaseTypeDeclarationSyntax updated = path.Count == 1
            ? AddMembers(target, null, members)
            : AddMembersToInnerType(target, path.Skip(1).ToArray(), members);
        return outerType.WithMembers(outerType.Members.Replace(target, updated));
    }
}
