using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using DiffPlex.DiffBuilder.Model;
using GrammarFixer.Models;

namespace GrammarFixer.UI;

/// <summary>Converts DiffType enum to a background brush for the CorrectionWindow diff list.</summary>
public class DiffColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DiffType type)
        {
            return type switch
            {
                DiffType.Insert => new SolidColorBrush(WpfColor.FromRgb(198, 239, 206)),
                DiffType.Delete => new SolidColorBrush(WpfColor.FromRgb(255, 199, 206)),
                DiffType.Modify => new SolidColorBrush(WpfColor.FromRgb(255, 235, 156)),
                _               => new SolidColorBrush(Colors.Transparent)
            };
        }
        return new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
