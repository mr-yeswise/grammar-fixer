namespace GrammarFixer.Models;

public record Edit
{
    public string Original    { get; init; } = string.Empty;
    public string Replacement { get; init; } = string.Empty;
    public string Message     { get; init; } = string.Empty;
}
