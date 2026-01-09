using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SshManager.App.Converters;

/// <summary>
/// Converts a string (typically a name) to a consistent color based on its hash.
/// This ensures the same name always gets the same color, creating stable visual associations.
/// </summary>
public class NameToColorConverter : IValueConverter
{
    private static readonly string[] Colors = new[]
    {
        "#E91E63", "#9C27B0", "#673AB7", "#3F51B5", "#2196F3",
        "#00BCD4", "#009688", "#4CAF50", "#8BC34A", "#FF9800",
        "#FF5722", "#795548", "#607D8B", "#F44336", "#03A9F4"
    };

    private static readonly SolidColorBrush GrayBrush = new(Color.FromRgb(0x88, 0x88, 0x88));

    static NameToColorConverter()
    {
        GrayBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string name && !string.IsNullOrEmpty(name))
        {
            int hash = Math.Abs(name.GetHashCode());
            string colorHex = Colors[hash % Colors.Length];

            try
            {
                var color = (Color)ColorConverter.ConvertFromString(colorHex);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch
            {
                return GrayBrush;
            }
        }

        return GrayBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
