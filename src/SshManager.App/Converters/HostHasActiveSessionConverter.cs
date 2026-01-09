using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Data;
using SshManager.Terminal;

namespace SshManager.App.Converters;

/// <summary>
/// Converts Host ID and Sessions collection to a boolean indicating if the host has an active session.
/// Uses MultiBinding: values[0] = hostId (Guid), values[1] = Sessions collection.
/// Returns true if an active session exists for the host, false otherwise.
/// </summary>
public class HostHasActiveSessionConverter : IMultiValueConverter
{
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
                    return true;
                }
            }
        }

        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
