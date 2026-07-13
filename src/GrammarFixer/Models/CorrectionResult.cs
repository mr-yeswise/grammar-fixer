namespace GrammarFixer.Models;

public record CorrectionResult
{
    public string     Original  { get; init; } = string.Empty;
    public string     Corrected { get; init; } = string.Empty;
    public List<Edit> Edits     { get; init; } = [];
    public bool       FromCache { get; init; }
}
