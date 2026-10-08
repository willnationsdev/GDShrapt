namespace GDShrapt.Converter;

internal static class FileSystemHelper
{
    public static string? FindFirstFileByPattern(string root, params ReadOnlySpan<string> patterns)
    {
        for (var directory = new DirectoryInfo(root); directory != null; directory = directory.Parent)
        {
            foreach (var pattern in patterns)
            {
                if (directory.EnumerateFiles(pattern, SearchOption.TopDirectoryOnly).Any())
                    return directory.FullName;
            }
        }

        return null;
    }

    public static string? FindSolutionRoot(string projectRoot) => FindFirstFileByPattern(projectRoot, ["*.sln", "*.slnx"]);

    public static bool IsSameOrChildPath(string rootPath, string path)
    {
        var relativePath = Path.GetRelativePath(rootPath, path);
        return relativePath == "." ||
               (relativePath != ".." &&
                !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !Path.IsPathRooted(relativePath));
    }
}

