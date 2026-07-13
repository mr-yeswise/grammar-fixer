using System;

namespace GrammarFixer.Models;

/// <summary>
/// Represents a single edit/correction applied to text.
/// </summary>
public sealed class Edit
{
    public string Original { get; init; } = "";
    public string Replacement { get; init; } = "";
    public string Message { get; init; } = "";
    public int Offset { get; init; }
    public int Length { get; init; }

    // Parameterless constructor for JSON deserialization
    public Edit() { }

    // Convenience constructor
    public Edit(string original, string replacement, string message, int offset, int length)
    {
        Original = original;
        Replacement = replacement;
        Message = message;
        Offset = offset;
        Length = length;
    }
}