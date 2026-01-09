using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SshManager.App.Services;

namespace SshManager.App.Converters;

/// <summary>
/// Converts Host ID and HostStatuses dictionary to a SolidColorBrush for the status indicator.
/// Uses MultiBinding: values[0] = hostId (Guid), values[1] = HostStatuses dictionary (triggers refresh).
/// Green = online, Red = offline, Gray = checking/unknown.
/// </summary>
public class HostStatusToColorConverter : IMultiValueConverter
{
    private static readonly SolidColorBrush OnlineBrush = new(Color.FromRgb(0x00, 0xCC, 0x00));   // Green
    private static readonly SolidColorBrush OfflineBrush = new(Color.FromRgb(0xCC, 0x00, 0x00)); // Red
    private static readonly SolidColorBrush UnknownBrush = new(Color.FromRgb(0x88, 0x88, 0x88)); // Gray

    static HostStatusToColorConverter()
    {
        OnlineBrush.Freeze();
        OfflineBrush.Freeze();
        UnknownBrush.Freeze();
    }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 1 && values[0] is Guid hostId)
        {
            // values[1] is the HostStatuses dictionary that triggers refresh
            if (values.Length >= 2 && values[1] is IReadOnlyDictionary<Guid, HostStatus> statuses)
            {
                if (statuses.TryGetValue(hostId, out var status))
                {
                    return status.IsOnline ? OnlineBrush : OfflineBrush;
                }
            }
        }

        return UnknownBrush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts Host ID and HostStatuses dictionary to a tooltip string with latency info.
/// Uses MultiBinding: values[0] = hostId (Guid), values[1] = HostStatuses dictionary (triggers refresh).
/// </summary>
public class HostStatusToTooltipConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 1 && values[0] is Guid hostId)
        {
            // values[1] is the HostStatuses dictionary that triggers refresh
            if (values.Length >= 2 && values[1] is IReadOnlyDictionary<Guid, HostStatus> statuses)
            {
                if (statuses.TryGetValue(hostId, out var status))
                {
                    if (status.IsOnline)
                    {
                        var latency = status.Latency?.TotalMilliseconds.ToString("F0") ?? "?";
                        return $"Online ({latency}ms)";
                    }
                    return "Offline";
                }
            }
        }

        return "Checking...";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
