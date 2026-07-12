using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GrammarFixer.Models;

namespace GrammarFixer.Core;

/// <summary>
/// Offline static correction engine.
/// Typo dictionary sourced from Wikipedia's "Wikipedia:Lists of common misspellings"
/// (https://en.wikipedia.org/wiki/Wikipedia:Lists_of_common_misspellings/For_machines)
/// bundled in Data/typos_en.json
/// </summary>
public sealed class StaticCorrectionEngine
{
    private Dictionary<string, string> _typos = new(StringComparer.OrdinalIgnoreCase);

    public void LoadDictionary(string jsonPath)
    {
        if (!File.Exists(jsonPath)) return;
        var json = File.ReadAllText(jsonPath);
        _typos = JsonSerializer.Deserialize<Dictionary<string, string>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public CorrectionResult Correct(string input)
    {
        var edits = new List<Edit>();
        var result = ApplyRules(input, edits);
        return new CorrectionResult(input, result, edits);
    }

    private string ApplyRules(string text, List<Edit> edits)
    {
        text = FixDoubleSpaces(text, edits);
        text = FixRepeatedWords(text, edits);
        text = FixLoneI(text, edits);
        text = FixCapitalizationAfterPeriod(text, edits);
        text = FixContractions(text, edits);
        text = FixTypos(text, edits);
        return text;
    }

    private static string FixDoubleSpaces(string text, List<Edit> edits)
    {
        var result = Regex.Replace(text, @"  +", " ");
        if (result != text)
            edits.Add(new Edit("  ", " ", "Double space removed", 0, 2));
        return result;
    }

    private static string FixRepeatedWords(string text, List<Edit> edits)
    {
        return Regex.Replace(text, @"\b(\w+)\s+\1\b", m =>
        {
            edits.Add(new Edit(m.Value, m.Groups[1].Value, "Repeated word", m.Index, m.Length));
            return m.Groups[1].Value;
        }, RegexOptions.IgnoreCase);
    }

    private static string FixLoneI(string text, List<Edit> edits)
    {
        return Regex.Replace(text, @"(?<![\w])i(?![\w])", m =>
        {
            edits.Add(new Edit("i", "I", "Lone 'i' should be capitalized", m.Index, 1));
            return "I";
        });
    }

    private static string FixCapitalizationAfterPeriod(string text, List<Edit> edits)
    {
        return Regex.Replace(text, @"([.!?]\s+)([a-z])", m =>
        {
            var replacement = m.Groups[1].Value + char.ToUpper(m.Groups[2].Value[0]);
            edits.Add(new Edit(m.Value, replacement, "Capitalize after sentence end", m.Index, m.Length));
            return replacement;
        });
    }

    private static readonly Dictionary<string, string> Contractions = new(StringComparer.OrdinalIgnoreCase)
    {
        { "dont", "don't" }, { "cant", "can't" }, { "wont", "won't" },
        { "isnt", "isn't" }, { "arent", "aren't" }, { "wasnt", "wasn't" },
        { "werent", "weren't" }, { "havent", "haven't" }, { "hasnt", "hasn't" },
        { "hadnt", "hadn't" }, { "wouldnt", "wouldn't" }, { "couldnt", "couldn't" },
        { "shouldnt", "shouldn't" }, { "doesnt", "doesn't" }, { "didnt", "didn't" },
        { "im", "I'm" }, { "ive", "I've" }, { "id", "I'd" }, { "ill", "I'll" },
        { "youre", "you're" }, { "youve", "you've" }, { "youd", "you'd" },
        { "youll", "you'll" }, { "theyre", "they're" }, { "theyve", "they've" },
        { "its", "it's" }, // context-sensitive; applied conservatively
        { "weve", "we've" }, { "wed", "we'd" }, { "well", "we'll" },
        { "hes", "he's" }, { "shes", "she's" }, { "heres", "here's" },
        { "thats", "that's" }, { "whats", "what's" }, { "whos", "who's" },
        { "theres", "there's" }, { "wheres", "where's" }
    };

    private static string FixContractions(string text, List<Edit> edits)
    {
        foreach (var (wrong, right) in Contractions)
        {
            var pattern = $@"\b{Regex.Escape(wrong)}\b";
            text = Regex.Replace(text, pattern, m =>
            {
                edits.Add(new Edit(m.Value, right, "Missing apostrophe in contraction", m.Index, m.Length));
                return right;
            }, RegexOptions.IgnoreCase);
        }
        return text;
    }

    private string FixTypos(string text, List<Edit> edits)
    {
        foreach (var (wrong, right) in _typos)
        {
            var pattern = $@"\b{Regex.Escape(wrong)}\b";
            text = Regex.Replace(text, pattern, m =>
            {
                var corrected = PreserveCase(m.Value, right);
                edits.Add(new Edit(m.Value, corrected, "Common misspelling", m.Index, m.Length));
                return corrected;
            });
        }
        return text;
    }

    private static string PreserveCase(string original, string replacement)
    {
        if (string.IsNullOrEmpty(original)) return replacement;
        if (char.IsUpper(original[0]))
            return char.ToUpper(replacement[0]) + replacement[1..];
        return replacement;
    }
}
