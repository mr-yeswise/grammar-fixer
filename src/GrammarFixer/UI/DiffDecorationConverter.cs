using System.Globalization;
using System.Windows;
using System.Windows.Data;
using GrammarFixer.Models;

namespace GrammarFixer.UI;

/// <summary>Converts DiffType to strikethrough for deleted diff segments.</summary>
public class DiffDecorationConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DiffType type && type == DiffType.Delete)
            return TextDecorations.Strikethrough;
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
