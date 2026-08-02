using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GDShrapt.LSP;

public class GDSelectionRangeParams
{
    [JsonPropertyName("textDocument")]
    public GDLspTextDocumentIdentifier TextDocument { get; set; } = new();

    [JsonPropertyName("positions")]
    public List<GDLspPosition> Positions { get; set; } = new();
}

public class GDSelectionRange
{
    [JsonPropertyName("range")]
    public GDLspRange Range { get; set; } = null!;

    [JsonPropertyName("parent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GDSelectionRange? Parent { get; set; }
}
