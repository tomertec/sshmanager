using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using SshManager.Core.Models;

namespace SshManager.App.Behaviors;

/// <summary>
/// Attached behavior that enables drag-and-drop reordering for ListBox items.
/// Supports reordering hosts within groups and moving hosts between groups.
/// </summary>
public static class ListBoxDragDropBehavior
{
    private static Point _startPoint;
    private static bool _isDragging;
    private static DragAdorner? _adorner;
    private static AdornerLayer? _adornerLayer;
    private static object? _draggedItem;
    private static ListBox? _sourceListBox;

    #region IsEnabled Attached Property

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ListBoxDragDropBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj)
    {
        return (bool)obj.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(DependencyObject obj, bool value)
    {
        obj.SetValue(IsEnabledProperty, value);
    }

    #endregion

    #region ReorderCommand Attached Property

    public static readonly DependencyProperty ReorderCommandProperty =
        DependencyProperty.RegisterAttached(
            "ReorderCommand",
            typeof(ICommand),
            typeof(ListBoxDragDropBehavior),
            new PropertyMetadata(null));

    public static ICommand GetReorderCommand(DependencyObject obj)
    {
        return (ICommand)obj.GetValue(ReorderCommandProperty);
    }

    public static void SetReorderCommand(DependencyObject obj, ICommand value)
    {
        obj.SetValue(ReorderCommandProperty, value);
    }

    #endregion

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox listBox)
            return;

        if ((bool)e.NewValue)
        {
            listBox.PreviewMouseLeftButtonDown += ListBox_PreviewMouseLeftButtonDown;
            listBox.PreviewMouseMove += ListBox_PreviewMouseMove;
            listBox.DragOver += ListBox_DragOver;
            listBox.Drop += ListBox_Drop;
            listBox.DragLeave += ListBox_DragLeave;
            listBox.AllowDrop = true;
        }
        else
        {
            listBox.PreviewMouseLeftButtonDown -= ListBox_PreviewMouseLeftButtonDown;
            listBox.PreviewMouseMove -= ListBox_PreviewMouseMove;
            listBox.DragOver -= ListBox_DragOver;
            listBox.Drop -= ListBox_Drop;
            listBox.DragLeave -= ListBox_DragLeave;
            listBox.AllowDrop = false;
        }
    }

    private static void ListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(null);
        _isDragging = false;
        _draggedItem = null;

        if (sender is not ListBox listBox)
            return;

        // Find the ListBoxItem that was clicked
        var element = e.OriginalSource as DependencyObject;

        // Don't start drag if clicking on an interactive element (button, textbox, etc.)
        if (element != null && IsInteractiveElement(element))
            return;

        var listBoxItem = FindAncestor<ListBoxItem>(element);

        if (listBoxItem != null && listBoxItem.DataContext is HostEntry)
        {
            _draggedItem = listBoxItem.DataContext;
            _sourceListBox = listBox;
        }
    }

    /// <summary>
    /// Checks if the element or any of its ancestors up to ListBoxItem is an interactive control
    /// that should not trigger drag operations.
    /// </summary>
    private static bool IsInteractiveElement(DependencyObject? element)
    {
        while (element != null)
        {
            // Check for various interactive control types
            if (element is System.Windows.Controls.Primitives.ButtonBase ||
                element is System.Windows.Controls.TextBox ||
                element is System.Windows.Controls.ComboBox ||
                element is System.Windows.Controls.Primitives.Thumb ||
                element is System.Windows.Controls.Slider)
            {
                return true;
            }

            // Stop searching at ListBoxItem level
            if (element is ListBoxItem)
                return false;

            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    private static void ListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedItem == null)
            return;

        var currentPosition = e.GetPosition(null);
        var diff = _startPoint - currentPosition;

        // Start drag only if the mouse has moved far enough
        if (!_isDragging && (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance))
        {
            _isDragging = true;

            if (sender is ListBox listBox)
            {
                StartDrag(listBox, _draggedItem);
            }
        }
    }

    private static void StartDrag(ListBox listBox, object draggedItem)
    {
        // Create adorner for visual feedback
        var element = listBox.ItemContainerGenerator.ContainerFromItem(draggedItem) as UIElement;
        if (element != null)
        {
            _adornerLayer = AdornerLayer.GetAdornerLayer(element);
            if (_adornerLayer != null)
            {
                _adorner = new DragAdorner(listBox, draggedItem)
                {
                    Opacity = 0.7
                };
                _adornerLayer.Add(_adorner);
            }
        }

        var data = new DataObject("HostEntry", draggedItem);
        DragDrop.DoDragDrop(listBox, data, DragDropEffects.Move);

        // Clean up adorner
        if (_adorner != null && _adornerLayer != null)
        {
            _adornerLayer.Remove(_adorner);
            _adorner = null;
            _adornerLayer = null;
        }

        _isDragging = false;
        _draggedItem = null;
        _sourceListBox = null;
    }

    private static void ListBox_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("HostEntry"))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        // Update adorner position
        if (_adorner != null && sender is ListBox listBox)
        {
            var position = e.GetPosition(listBox);
            _adorner.UpdatePosition(position);
        }
    }

    private static void ListBox_DragLeave(object sender, DragEventArgs e)
    {
        // Clean up adorner if drag leaves the ListBox
        if (_adorner != null && _adornerLayer != null)
        {
            var listBox = sender as ListBox;
            if (listBox != null && !IsMouseOverListBox(listBox))
            {
                _adornerLayer.Remove(_adorner);
                _adorner = null;
                _adornerLayer = null;
            }
        }
    }

    private static bool IsMouseOverListBox(ListBox listBox)
    {
        var position = Mouse.GetPosition(listBox);
        var bounds = VisualTreeHelper.GetDescendantBounds(listBox);
        return bounds.Contains(position);
    }

    private static void ListBox_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("HostEntry"))
            return;

        var droppedItem = e.Data.GetData("HostEntry") as HostEntry;
        if (droppedItem == null)
            return;

        var listBox = sender as ListBox;
        if (listBox == null)
            return;

        // Find the target item (item being dropped on)
        var targetElement = e.OriginalSource as DependencyObject;
        var targetListBoxItem = FindAncestor<ListBoxItem>(targetElement);
        var targetItem = targetListBoxItem?.DataContext as HostEntry;

        // Get the reorder command
        var reorderCommand = GetReorderCommand(listBox);
        if (reorderCommand != null && reorderCommand.CanExecute(null))
        {
            // Execute the command with the dropped and target items
            // Use listBox as reference if targetListBoxItem is null (dropped on empty space)
            var parameter = new DragDropReorderEventArgs
            {
                DroppedItem = droppedItem,
                TargetItem = targetItem,
                DropPosition = e.GetPosition((IInputElement?)targetListBoxItem ?? listBox)
            };
            reorderCommand.Execute(parameter);
        }

        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T ancestor)
                return ancestor;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}

/// <summary>
/// Event arguments for drag-drop reorder operations.
/// </summary>
public sealed class DragDropReorderEventArgs
{
    /// <summary>
    /// The item that was dropped.
    /// </summary>
    public required HostEntry DroppedItem { get; init; }

    /// <summary>
    /// The item that was dropped on (null if dropped on empty space).
    /// </summary>
    public HostEntry? TargetItem { get; init; }

    /// <summary>
    /// The position where the drop occurred relative to the target item.
    /// </summary>
    public Point DropPosition { get; init; }
}
