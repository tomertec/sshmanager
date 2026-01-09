using System.Windows;

namespace SshManager.App.Converters;

/// <summary>
/// A proxy that allows binding to a DataContext from elements outside the visual tree,
/// such as ContextMenu and ToolTip which exist in separate visual trees.
/// </summary>
public class BindingProxy : Freezable
{
    protected override Freezable CreateInstanceCore()
    {
        return new BindingProxy();
    }

    public object Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(
            nameof(Data),
            typeof(object),
            typeof(BindingProxy),
            new UIPropertyMetadata(null));
}
