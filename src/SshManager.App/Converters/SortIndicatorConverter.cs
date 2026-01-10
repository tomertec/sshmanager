using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using SshManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace SshManager.App.Converters;

/// <summary>
/// Converts sort column and direction to a visibility for sort indicator icons.
/// </summary>
public class SortIndicatorVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || parameter is not string columnName)
            return Visibility.Collapsed;

        var currentColumn = values[0] as FileSortColumn?;
        var targetColumn = Enum.TryParse<FileSortColumn>(columnName, out var col) ? col : FileSortColumn.Name;

        return currentColumn == targetColumn ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts sort direction to an appropriate arrow symbol.
/// </summary>
public class SortDirectionToSymbolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ListSortDirection direction)
        {
            return direction == ListSortDirection.Ascending
                ? SymbolRegular.ChevronUp20
                : SymbolRegular.ChevronDown20;
        }
        return SymbolRegular.ChevronUp20;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
