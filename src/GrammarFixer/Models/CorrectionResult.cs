using System.Collections.Generic;

namespace GrammarFixer.Models;

/// <summary>
/// Result of a grammar correction operation.
/// </summary>
public sealed class CorrectionResult
{
    public string Original { get; init; } = "";
    public string Corrected { get; init; } = "";
    public List<Edit> Edits { get; init; } = [];
    public bool FromCache { get; init; }

    // Parameterless constructor for JSON deserialization
    public CorrectionResult() { }

    // Convenience constructor
    public CorrectionResult(string original, string corrected, List<Edit> edits, bool fromCache = false)
    {
        Original = original;
        Corrected = corrected;
        Edits = edits;
        FromCache = fromCache;
    }

    /// <summary>Creates a cache-hit copy.</summary>
    public CorrectionResult WithCacheHit()
    {
        return new CorrectionResult
        {
            Original = Original,
            Corrected = Corrected,
            Edits = Edits,
            FromCache = true
        };
    }
}