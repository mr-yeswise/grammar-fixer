namespace GrammarFixer.Models;

public record CorrectionResult(
    string Original,
    string Corrected,
    List<Edit> Edits,
    bool FromCache = false
);
