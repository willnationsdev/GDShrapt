namespace GDShrapt.Plugin;

/// <summary>
/// Converts between Godot editor positions (0-based line and column) and the CLI.Core handler
/// position contract. Handler INPUT is 1-based line/column; OUTPUT columns differ per DTO
/// (see GDShrapt.CLI.Core position conventions): find-refs columns are 1-based, go-to-def columns
/// are 0-based, rename/edit columns are 1-based. Output lines are always 1-based.
/// </summary>
public static class GDPluginPositionAdapter
{
    /// <summary>Godot 0-based line → handler 1-based input line.</summary>
    public static int ToHandlerLine(int godotLine) => godotLine + 1;

    /// <summary>Godot 0-based column → handler 1-based input column.</summary>
    public static int ToHandlerColumn(int godotColumn) => godotColumn + 1;

    /// <summary>Handler 1-based output line → Godot 0-based line.</summary>
    public static int FromLine(int handlerLine) => handlerLine - 1;

    /// <summary>find-refs (GDCliReferenceLocation) 1-based column → Godot 0-based column.</summary>
    public static int FromRefColumn(int oneBasedColumn) => oneBasedColumn - 1;

    /// <summary>go-to-def (GDDefinitionLocation) column is already 0-based — identity.</summary>
    public static int FromDefinitionColumn(int zeroBasedColumn) => zeroBasedColumn;

    /// <summary>rename/format edit (GDTextEdit) 1-based column → Godot 0-based column.</summary>
    public static int FromEditColumn(int oneBasedColumn) => oneBasedColumn - 1;
}
