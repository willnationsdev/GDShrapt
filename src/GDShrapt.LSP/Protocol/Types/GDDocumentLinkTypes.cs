using System.Text.Json.Serialization;

namespace GDShrapt.LSP;

public class GDDocumentLinkParams
{
    [JsonPropertyName("textDocument")]
    public GDLspTextDocumentIdentifier TextDocument { get; set; } = new();
}

public class GDDocumentLink
{
    [JsonPropertyName("range")]
    public GDLspRange Range { get; set; } = null!;

    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Target { get; set; }
}

public class GDDocumentLinkOptions
{
    [JsonPropertyName("resolveProvider")]
    public bool ResolveProvider { get; set; }
}
