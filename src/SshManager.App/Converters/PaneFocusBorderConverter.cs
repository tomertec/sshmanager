using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SshManager.Core.Models;

namespace SshManager.App.Converters;

/// <summary>
/// Converts pane focus state and group color to a border brush.
/// Uses MultiBinding: values[0] = IsFocused (bool), values[1] = Group.Color (string, hex format).
/// When focused: uses group color if available, otherwise default blue (#0078D4).
/// When not focused: uses inactive color (#2A2A2A).
/// </summary>
public class PaneFocusBorderConverter : IMultiValueConverter
{
    private static readonly SolidColorBrush DefaultFocusedBrush = new(Color.FromRgb(0x00, 0x78, 0xD4));  // Blue #0078D4
    private static readonly SolidColorBrush InactiveBrush = new(Color.FromRgb(0x2A, 0x2A, 0x2A));  // Dark gray #2A2A2A

    static PaneFocusBorderConverter()
    {
        DefaultFocusedBrush.Freeze();
        InactiveBrush.Freeze();
    }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // values[0] = IsFocused (bool)
        // values[1] = Session.Host.Group.Color (string, hex format)

        var isFocused = values.Length > 0 && values[0] is bool focused && focused;

        if (!isFocused)
        {
            return InactiveBrush;
        }

        // Focused - check if we have a group color
        if (values.Length > 1 && values[1] is string hexColor && !string.IsNullOrWhiteSpace(hexColor))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hexColor);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch
            {
                // Invalid color format, fall through to default
            }
        }

        return DefaultFocusedBrush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts session tab selection state and group color to a border brush.
/// Uses MultiBinding: values[0] = IsSelected (bool), values[1] = Host.Group.Color (string, hex format),
/// values[2] = IsSelectedForBroadcast (bool, optional), values[3] = Host.ConnectionType (ConnectionType, optional).
/// Priority: Broadcast (orange) > Selected (group color or serial yellow or blue) > Not selected (transparent).
/// </summary>
public class SessionTabBorderConverter : IMultiValueConverter
{
    private static readonly SolidColorBrush DefaultSelectedBrush = new(Color.FromRgb(0x00, 0x78, 0xD4));  // Blue #0078D4
    private static readonly SolidColorBrush SerialSelectedBrush = new(Color.FromRgb(0xD7, 0xF2, 0x27));  // Yellow-green #D7F227
    private static readonly SolidColorBrush BroadcastBrush = new(Color.FromRgb(0xFF, 0x55, 0x00));  // Orange #FF5500
    private static readonly SolidColorBrush TransparentBrush = Brushes.Transparent;

    static SessionTabBorderConverter()
    {
        DefaultSelectedBrush.Freeze();
        SerialSelectedBrush.Freeze();
        BroadcastBrush.Freeze();
    }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // values[0] = IsSelected (bool)
        // values[1] = Host.Group.Color (string, hex format)
        // values[2] = IsSelectedForBroadcast (bool, optional)
        // values[3] = Host.ConnectionType (ConnectionType, optional)

        // Check broadcast first (highest priority)
        var isSelectedForBroadcast = values.Length > 2 && values[2] is bool broadcast && broadcast;
        if (isSelectedForBroadcast)
        {
            return BroadcastBrush;
        }

        var isSelected = values.Length > 0 && values[0] is bool selected && selected;

        if (!isSelected)
        {
            return TransparentBrush;
        }

        // Selected - check if we have a group color
        if (values.Length > 1 && values[1] is string hexColor && !string.IsNullOrWhiteSpace(hexColor))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hexColor);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch
            {
                // Invalid color format, fall through to default
            }
        }

        // Check if serial session - use yellow
        var isSerialSession = values.Length > 3 && values[3] is ConnectionType connType && connType == ConnectionType.Serial;
        if (isSerialSession)
        {
            return SerialSelectedBrush;
        }

        return DefaultSelectedBrush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
