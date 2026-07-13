using System;
using System.Globalization;
using System.Windows.Data;
using DiffPlex.Model;

namespace GrammarFixer.UI;

/// <summary>
/// Converts DiffPlex change types to foreground colors for inline diff display.
/// </summary>
public class DiffColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ChangeType type)
        {
            return type switch
            {
                ChangeType.Inserted => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4E, 0xC4, 0x6E)),  // Green
                ChangeType.Deleted => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF4, 0x47, 0x47)),  // Red
                ChangeType.Imaginary => System.Windows.Media.Brushes.Gray,
                _ => System.Windows.Media.Brushes.White
            };
        }
        return System.Windows.Media.Brushes.White;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}