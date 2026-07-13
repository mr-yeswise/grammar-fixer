namespace GrammarFixer.Models;

public enum UxMode
{
    /// <summary>One click on the pill applies all corrections instantly (Quillbot style).</summary>
    OneClickRewrite,
    /// <summary>Show the overlay diff window for manual review before applying.</summary>
    ReviewSuggestions
}
