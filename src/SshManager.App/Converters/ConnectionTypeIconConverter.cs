using System.Globalization;
using System.Windows.Data;
using SshManager.Core.Models;
using Wpf.Ui.Controls;

namespace SshManager.App.Converters;

/// <summary>
/// Converts ConnectionType to an appropriate SymbolIcon.
/// </summary>
public class ConnectionTypeIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ConnectionType connectionType)
        {
            return connectionType switch
            {
                ConnectionType.Serial => SymbolRegular.PlugConnected24,
                _ => SymbolRegular.Desktop24 // SSH
            };
        }
        
        return SymbolRegular.Desktop24;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
