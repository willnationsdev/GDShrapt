using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GDShrapt.LSP;

/// <summary>Parameters for workspace/willRenameFiles (and didRenameFiles).</summary>
public class GDRenameFilesParams
{
    [JsonPropertyName("files")]
    public List<GDFileRename> Files { get; set; } = new();
}

public class GDFileRename
{
    [JsonPropertyName("oldUri")]
    public string OldUri { get; set; } = "";

    [JsonPropertyName("newUri")]
    public string NewUri { get; set; } = "";
}

/// <summary>Server capability: workspace.fileOperations.willRename.</summary>
public class GDWorkspaceServerCapabilities
{
    [JsonPropertyName("fileOperations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GDFileOperationsServerCapabilities? FileOperations { get; set; }
}

public class GDFileOperationsServerCapabilities
{
    [JsonPropertyName("willRename")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GDFileOperationRegistrationOptions? WillRename { get; set; }
}

public class GDFileOperationRegistrationOptions
{
    [JsonPropertyName("filters")]
    public List<GDFileOperationFilter> Filters { get; set; } = new();
}

public class GDFileOperationFilter
{
    [JsonPropertyName("pattern")]
    public GDFileOperationPattern Pattern { get; set; } = new();
}

public class GDFileOperationPattern
{
    [JsonPropertyName("glob")]
    public string Glob { get; set; } = "";
}
