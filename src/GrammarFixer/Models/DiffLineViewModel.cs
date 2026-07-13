namespace GrammarFixer.Models;

public enum DiffType { None, Insert, Delete, Modify }

public class DiffLineViewModel
{
    public string   Text { get; set; } = string.Empty;
    public DiffType Type { get; set; } = DiffType.None;
}
