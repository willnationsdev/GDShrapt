using System.Text.RegularExpressions;

namespace GDShrapt.Converter.Planning;

internal static class GDConversionGitAttributes
{
    public static string GetLineEnding(string outputPath, string rootDirectory, string fallback)
    {
        var fullRoot = Path.GetFullPath(rootDirectory);
        var fullPath = Path.GetFullPath(outputPath);
        if (!FileSystemHelper.IsSameOrChildPath(fullRoot, fullPath))
            return fallback;

        var directories = new Stack<string>();
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(fullPath)!);
             directory != null && FileSystemHelper.IsSameOrChildPath(fullRoot, directory.FullName);
             directory = directory.Parent)
        {
            directories.Push(directory.FullName);
            if (string.Equals(directory.FullName, fullRoot, GetPathComparison()))
                break;
        }

        var lineEnding = fallback;
        while (directories.Count > 0)
        {
            var directory = directories.Pop();
            var attributesPath = Path.Combine(directory, ".gitattributes");
            if (!File.Exists(attributesPath))
                continue;

            var relative = Path.GetRelativePath(directory, fullPath).Replace(Path.DirectorySeparatorChar, '/');
            foreach (var rawLine in File.ReadLines(attributesPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;

                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !Matches(parts[0], relative))
                    continue;

                foreach (var attribute in parts.Skip(1))
                {
                    if (attribute.Equals("eol=lf", StringComparison.OrdinalIgnoreCase))
                        lineEnding = "\n";
                    else if (attribute.Equals("eol=crlf", StringComparison.OrdinalIgnoreCase))
                        lineEnding = "\r\n";
                    else if (attribute.Equals("-eol", StringComparison.OrdinalIgnoreCase))
                        lineEnding = fallback;
                }
            }
        }

        return lineEnding;
    }

    private static bool Matches(string pattern, string path)
    {
        if (pattern.StartsWith('!'))
            return false;
        var normalized = pattern.TrimStart('/');
        var target = normalized.Contains('/') ? path : Path.GetFileName(path);
        var regex = Regex.Escape(normalized)
            .Replace(@"\*\*/", "(?:.*/)?")
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", "[^/]*")
            .Replace(@"\?", "[^/]");
        return Regex.IsMatch(target, $"^{regex}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static StringComparison GetPathComparison()
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
