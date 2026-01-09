using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SshManager.Terminal;

namespace SshManager.App.Converters;

/// <summary>
/// Converts Host ID and Sessions collection to a SolidColorBrush for the host card border.
/// Uses MultiBinding: values[0] = hostId (Guid), values[1] = Sessions collection.
/// Blue = has active session, Transparent = no active session.
/// </summary>
public class HostActiveSessionBorderConverter : IMultiValueConverter
{
    private static readonly SolidColorBrush ActiveBrush = new(Color.FromRgb(0x00, 0x78, 0xD4));  // Blue #0078D4
    private static readonly SolidColorBrush InactiveBrush = Brushes.Transparent;

    static HostActiveSessionBorderConverter()
    {
        ActiveBrush.Freeze();
    }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 &&
            values[0] is Guid hostId &&
            values[1] is ObservableCollection<TerminalSession> sessions)
        {
            foreach (var session in sessions)
            {
                if (session.Host?.Id == hostId)
                {
                    return ActiveBrush;
                }
            }
        }

        return InactiveBrush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
