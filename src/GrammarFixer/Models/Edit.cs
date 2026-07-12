namespace GrammarFixer.Models;

public record Edit(
    string Original,
    string Replacement,
    string Reason,
    int Offset,
    int Length
);
