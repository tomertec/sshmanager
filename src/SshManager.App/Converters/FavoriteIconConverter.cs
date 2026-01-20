using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace SshManager.App.Converters;

/// <summary>
/// Converts a boolean IsFavorite value to a star icon.
/// </summary>
public class FavoriteIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isFavorite && isFavorite)
        {
            return SymbolRegular.Star24;
        }
        
        return SymbolRegular.StarOff24;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
