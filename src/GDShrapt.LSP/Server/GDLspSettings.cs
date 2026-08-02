using System.Text.Json;

namespace GDShrapt.LSP;

/// <summary>
/// Server-side tunables supplied by the client via the initialize request's
/// <c>initializationOptions</c>. All values fall back to defaults that preserve the
/// previous hardcoded behavior, so an empty/absent options object changes nothing.
/// </summary>
public sealed class GDLspSettings
{
    /// <summary>Debounce before fast syntax/lint diagnostics (ms). Default 300.</summary>
    public int SyntaxDebounceMs { get; init; } = 300;

    /// <summary>Debounce before full semantic diagnostics (ms). Default 800.</summary>
    public int SemanticDebounceMs { get; init; } = 800;

    /// <summary>Per-request operation timeout (ms). 0 disables timeouts (default).</summary>
    public int OperationTimeoutMs { get; init; }

    /// <summary>
    /// Parses settings from the initialize request's <c>initializationOptions</c>.
    /// The value is an opaque <see cref="object"/> that System.Text.Json materializes as a
    /// <see cref="JsonElement"/>. Unknown/invalid fields are ignored.
    /// </summary>
    public static GDLspSettings FromInitializationOptions(object? options)
    {
        if (options is not JsonElement el || el.ValueKind != JsonValueKind.Object)
            return new GDLspSettings();

        return new GDLspSettings
        {
            SyntaxDebounceMs = ReadNonNegativeInt(el, "syntaxDebounceMs", 300),
            SemanticDebounceMs = ReadNonNegativeInt(el, "semanticDebounceMs", 800),
            OperationTimeoutMs = ReadNonNegativeInt(el, "operationTimeoutMs", 0)
        };
    }

    private static int ReadNonNegativeInt(JsonElement el, string name, int fallback)
    {
        if (!el.TryGetProperty(name, out var v))
            return fallback;

        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n >= 0)
            return n;

        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s) && s >= 0)
            return s;

        return fallback;
    }
}
