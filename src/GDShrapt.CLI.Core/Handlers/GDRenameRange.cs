using System.IO;
using GDShrapt.Semantics;

namespace GDShrapt.CLI.Core;

/// <summary>
/// The editable identifier range for a prepare-rename request. Positions are 1-based.
/// </summary>
public sealed class GDRenameRange
{
    /// <summary>1-based line of the identifier.</summary>
    public int Line { get; init; }

    /// <summary>1-based start column of the identifier.</summary>
    public int Column { get; init; }

    /// <summary>The identifier text shown in the rename box.</summary>
    public string Placeholder { get; init; } = "";
}

/// <summary>
/// Resolves the renameable identifier range at a position. Shared by the Base and Pro
/// rename handlers so prepare-rename behaves identically regardless of license.
/// </summary>
public static class GDRenameRangeResolver
{
    /// <summary>
    /// Resolves the identifier token under the cursor. Positions are 1-based.
    /// Returns null if the position is not on an identifier (rename not offered).
    /// </summary>
    public static GDRenameRange? Resolve(GDScriptProject project, string filePath, int line, int column)
    {
        var fullPath = Path.GetFullPath(filePath);
        var model = project.GetScript(fullPath)?.SemanticModel;
        if (model == null)
            return null;

        var identifier = model.GetIdentifierAtPosition(line - 1, column - 1);
        if (identifier == null)
            return null;

        return new GDRenameRange
        {
            Line = identifier.StartLine + 1,
            Column = identifier.StartColumn + 1,
            Placeholder = identifier.Sequence ?? ""
        };
    }
}
