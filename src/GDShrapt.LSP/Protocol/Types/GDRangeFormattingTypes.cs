using System.Text.Json.Serialization;

namespace GDShrapt.LSP;

/// <summary>
/// Parameters for textDocument/rangeFormatting.
/// </summary>
public class GDDocumentRangeFormattingParams
{
    [JsonPropertyName("textDocument")]
    public GDLspTextDocumentIdentifier TextDocument { get; set; } = new();

    [JsonPropertyName("range")]
    public GDLspRange Range { get; set; } = null!;

    [JsonPropertyName("options")]
    public GDFormattingOptions Options { get; set; } = new();
}
