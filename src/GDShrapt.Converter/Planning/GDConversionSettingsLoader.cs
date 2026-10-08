using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace GDShrapt.Converter.Planning;

internal static class GDConversionSettingsLoader
{
    public static GDConversionFormattingOptions Load(
        string projectDirectory,
        string solutionRoot,
        ICsProjectContext project)
    {
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var editorConfig in FindEditorConfigFiles(projectDirectory))
        {
            foreach (var (key, value) in ReadEditorConfig(editorConfig, projectDirectory))
                settings[key] = value;
        }
        var indentStyle = GetSetting(settings, "indent_style");
        var indentSizeValue = GetSetting(settings, "indent_size");
        var lineEndingValue = GetSetting(settings, "end_of_line");
        var namespaceStyle = GetSetting(settings, "csharp_style_namespace_declarations");
        var namingStyle = ResolveFieldNamingStyle(settings);
        var namingRules = ResolveNamingRules(settings);

        var resharperSettings = FindReSharperSettings(projectDirectory, solutionRoot);
        if (resharperSettings != null)
        {
            var resharper = ReadReSharperSettings(resharperSettings);
            indentStyle ??= FindSetting(resharper, "INDENT_STYLE");
            indentSizeValue ??= FindSetting(resharper, "INDENT_SIZE");
            lineEndingValue ??= FindSetting(resharper, "LINE_ENDING");
            namespaceStyle ??= FindSetting(resharper, "NAMESPACE_DECLARATION_STYLE");
            namingStyle ??= ParseNameStyle(FindSetting(resharper, "FIELD_NAMING_STYLE"));
            if (namingRules.Count == 0)
            {
                namingRules = FindSettings(resharper, "FIELD_NAMING_STYLE")
                    .Select(value => ParseNameStyle(value))
                    .Where(style => style != null)
                    .Select((style, index) => new GDConversionNamingRule(
                        new HashSet<string>(["field"], StringComparer.OrdinalIgnoreCase),
                        new HashSet<string>(["private"], StringComparer.OrdinalIgnoreCase),
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                        ToNamingStyle(style!.Value),
                        index))
                    .ToList();
            }
        }

        indentStyle ??= GetProperty(project, "IndentStyle", "UseTabs");
        indentSizeValue ??= GetProperty(project, "IndentSize");
        lineEndingValue ??= GetProperty(project, "EndOfLine");
        namespaceStyle ??= GetProperty(project, "NamespaceStyle", "PreferFileScopedNamespaces");
        namingStyle ??= ParseNameStyle(GetProperty(project, "PrivateFieldNamingStyle"));

        return new GDConversionFormattingOptions
        {
            UseTabIndent = indentStyle?.Contains("tab", StringComparison.OrdinalIgnoreCase) == true,
            IndentSize = ParsePositiveInt(indentSizeValue, 4),
            LineEnding = ParseLineEnding(lineEndingValue),
            GitAttributesRoot = FindGitRoot(solutionRoot),
            PreferFileScopedNamespaces = namespaceStyle?.Contains("file_scoped", StringComparison.OrdinalIgnoreCase) == true,
            FieldNameStyle = namingStyle ?? GDConversionNameStyle.PascalCase,
            NamingRules = namingRules
        };
    }

    private static IEnumerable<string> FindEditorConfigFiles(string projectDirectory)
    {
        var editorConfigs = new List<string>();
        for (var directory = new DirectoryInfo(projectDirectory); directory != null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, ".editorconfig");
            if (!File.Exists(path))
                continue;

            editorConfigs.Add(path);
            if (File.ReadLines(path)
                .Any(line => line.Trim().Equals("root = true", StringComparison.OrdinalIgnoreCase)))
            {
                break;
            }
        }

        editorConfigs.Reverse();
        return editorConfigs;
    }

    private static IEnumerable<KeyValuePair<string, string>> ReadEditorConfig(string path, string projectDirectory)
    {
        var relativeFile = Path.GetRelativePath(
                Path.GetDirectoryName(path)!,
                Path.Combine(projectDirectory, "__Generated__.cs"))
            .Replace(Path.DirectorySeparatorChar, '/');
        var sectionMatches = true;

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                sectionMatches = GlobMatches(line[1..^1], relativeFile);
                continue;
            }

            if (!sectionMatches)
                continue;

            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;

            yield return new KeyValuePair<string, string>(
                line[..separator].Trim(),
                line[(separator + 1)..].Trim());
        }
    }

    private static bool GlobMatches(string pattern, string path)
    {
        var regex = Regex.Escape(pattern.Replace('\\', '/'))
            .Replace(@"\*\*/", "(?:.*/)?")
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", "[^/]*")
            .Replace(@"\?", "[^/]");
        return Regex.IsMatch(path, $"^{regex}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static List<GDConversionNamingRule> ResolveNamingRules(IReadOnlyDictionary<string, string> settings)
    {
        var rules = new List<GDConversionNamingRule>();
        var order = 0;
        foreach (var (key, value) in settings)
        {
            if (!key.StartsWith("dotnet_naming_rule.", StringComparison.OrdinalIgnoreCase) ||
                !key.EndsWith(".symbols", StringComparison.OrdinalIgnoreCase))
                continue;

            var ruleName = key["dotnet_naming_rule.".Length..^".symbols".Length];
            var symbolName = value;
            var kinds = GetSetting(settings, $"dotnet_naming_symbols.{symbolName}.applicable_kinds");
            var accessibility = GetSetting(settings, $"dotnet_naming_symbols.{symbolName}.applicable_accessibilities");
            var styleName = GetSetting(settings, $"dotnet_naming_rule.{ruleName}.style");
            if (styleName == null)
                continue;
            var prefix = GetSetting(settings, $"dotnet_naming_style.{styleName}.required_prefix") ?? string.Empty;
            var suffix = GetSetting(settings, $"dotnet_naming_style.{styleName}.required_suffix") ?? string.Empty;
            var separator = GetSetting(settings, $"dotnet_naming_style.{styleName}.word_separator") ?? string.Empty;
            var capitalization = GetSetting(settings, $"dotnet_naming_style.{styleName}.capitalization");
            rules.Add(new GDConversionNamingRule(
                ParseNameList(kinds, allowAll: true),
                ParseNameList(accessibility, allowAll: true),
                ParseNameList(GetSetting(settings, $"dotnet_naming_symbols.{symbolName}.required_modifiers")),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new GDConversionNamingStyle(capitalization ?? "pascal_case", prefix, suffix, separator),
                order++));
        }

        return rules;
    }

    private static GDConversionNameStyle? ResolveFieldNamingStyle(IReadOnlyDictionary<string, string> settings)
    {
        var rule = ResolveNamingRules(settings).FirstOrDefault(candidate => candidate.AppliesTo(
            new GDConversionNamingContext("field", "private", new HashSet<string>(StringComparer.OrdinalIgnoreCase))));
        return rule == null ? null : ParseNameStyle(rule.Style.Capitalization, rule.Style.RequiredPrefix);
    }

    private static HashSet<string> ParseNameList(string? value, bool allowAll = false)
        => string.IsNullOrWhiteSpace(value)
            ? new HashSet<string>(allowAll ? ["*"] : [], StringComparer.OrdinalIgnoreCase)
            : value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static GDConversionNamingStyle ToNamingStyle(GDConversionNameStyle style)
        => style switch
        {
            GDConversionNameStyle.PascalCase => new GDConversionNamingStyle("pascal_case", "", "", ""),
            GDConversionNameStyle.PrivatePascalCase => new GDConversionNamingStyle("pascal_case", "_", "", ""),
            GDConversionNameStyle.CamelCase => new GDConversionNamingStyle("camel_case", "", "", ""),
            _ => new GDConversionNamingStyle("camel_case", "_", "", "")
        };

    private static IEnumerable<string> FindSettings(IReadOnlyDictionary<string, string> settings, string suffix)
        => settings.Where(pair => pair.Key.Contains(suffix, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value);

    private static GDConversionNameStyle? ParseNameStyle(string? style, string? prefix = null)
    {
        if (!string.IsNullOrEmpty(prefix) && prefix.StartsWith('_'))
        {
            return style?.ToLowerInvariant() switch
            {
                "pascal_case" or "pascalcase" => GDConversionNameStyle.PrivatePascalCase,
                _ => GDConversionNameStyle.PrivateCamelCase
            };
        }
        return style?.ToLowerInvariant() switch
        {
            "pascal_case" or "pascalcase" => GDConversionNameStyle.PascalCase,
            "camel_case" or "camelcase" => GDConversionNameStyle.CamelCase,
            _ => null
        };
    }

    private static string? FindReSharperSettings(string projectDirectory, string solutionRoot)
    {
        var directory = new DirectoryInfo(projectDirectory);
        while (directory != null && FileSystemHelper.IsSameOrChildPath(solutionRoot, directory.FullName))
        {
            var settings = directory.EnumerateFiles("*.sln.DotSettings", SearchOption.TopDirectoryOnly)
                .Concat(directory.EnumerateFiles("*.csproj.DotSettings", SearchOption.TopDirectoryOnly))
                .FirstOrDefault();
            if (settings != null)
                return settings.FullName;
            if (string.Equals(directory.FullName, solutionRoot, GetPathComparison()))
                break;
            directory = directory.Parent;
        }

        return null;
    }

    private static Dictionary<string, string> ReadReSharperSettings(string path)
    {
        var document = XDocument.Load(path);
        return document.Descendants()
            .Select(element => (Key: element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Key")?.Value,
                Value: element.Value.Trim()))
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase);
    }

    private static string? FindSetting(IReadOnlyDictionary<string, string> settings, string suffix)
        => settings.FirstOrDefault(pair => pair.Key.Contains(suffix, StringComparison.OrdinalIgnoreCase)).Value;

    private static string? GetSetting(IReadOnlyDictionary<string, string> settings, string key)
        => settings.TryGetValue(key, out var value) ? value : null;

    private static int ParsePositiveInt(string? value, int fallback)
        => int.TryParse(value, out var result) && result > 0 ? result : fallback;

    private static string ParseLineEnding(string? value)
        => value?.ToLowerInvariant() switch
        {
            "lf" => "\n",
            "cr" => "\r",
            "crlf" => "\r\n",
            _ => Environment.NewLine
        };

    private static string? GetProperty(ICsProjectContext project, params string[] names)
    {
        foreach (var name in names)
        {
            if (project.EvaluatedProperties.TryGetValue(name, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                if (name == "UseTabs")
                    return value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "tab" : "space";
                if (name == "PreferFileScopedNamespaces")
                    return value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "file_scoped" : "block_scoped";
                return value;
            }
        }

        return null;
    }

    private static StringComparison GetPathComparison()
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string FindGitRoot(string directory)
    {
        for (var current = new DirectoryInfo(directory); current != null; current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }
        }

        return directory;
    }
}
