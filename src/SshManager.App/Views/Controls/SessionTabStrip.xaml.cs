using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SshManager.Terminal;

namespace SshManager.App.Views.Controls;

/// <summary>
/// A horizontal strip of session tabs for switching between terminal sessions.
/// Supports broadcast mode indicators, group color coding, and smooth horizontal scrolling
/// with chevron navigation buttons when tabs overflow the available width.
/// </summary>
public partial class SessionTabStrip : UserControl
{
    private const double ScrollStep = 120.0;

    /// <summary>
    /// Event raised when the selected session tab changes.
    /// </summary>
    public event EventHandler<TerminalSession?>? SessionSelectionChanged;

    private ScrollViewer? _scrollViewer;

    public SessionTabStrip()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _scrollViewer = FindScrollViewer(SessionTabs);
        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer.ScrollChanged += OnScrollChanged;
        }
        UpdateScrollButtonVisibility();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
        }
    }

    private void SessionTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem is TerminalSession session)
        {
            SessionSelectionChanged?.Invoke(this, session);
            // Scroll the selected tab into view
            listBox.ScrollIntoView(session);
        }
        else
        {
            SessionSelectionChanged?.Invoke(this, null);
        }

        // Update button visibility after selection change (layout may shift)
        Dispatcher.InvokeAsync(UpdateScrollButtonVisibility,
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void SessionTabs_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_scrollViewer == null)
            return;

        // Convert vertical mouse wheel to horizontal scroll
        _scrollViewer.ScrollToHorizontalOffset(
            _scrollViewer.HorizontalOffset - e.Delta);
        e.Handled = true;

        UpdateScrollButtonVisibility();
    }

    private void ScrollLeftButton_Click(object sender, RoutedEventArgs e)
    {
        if (_scrollViewer == null)
            return;

        _scrollViewer.ScrollToHorizontalOffset(
            _scrollViewer.HorizontalOffset - ScrollStep);
        UpdateScrollButtonVisibility();
    }

    private void ScrollRightButton_Click(object sender, RoutedEventArgs e)
    {
        if (_scrollViewer == null)
            return;

        _scrollViewer.ScrollToHorizontalOffset(
            _scrollViewer.HorizontalOffset + ScrollStep);
        UpdateScrollButtonVisibility();
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateScrollButtonVisibility();
    }

    private void UpdateScrollButtonVisibility()
    {
        if (_scrollViewer == null)
            return;

        var canScrollLeft = _scrollViewer.HorizontalOffset > 0;
        var canScrollRight = _scrollViewer.HorizontalOffset
            < _scrollViewer.ScrollableWidth - 1;

        ScrollLeftButton.Visibility = canScrollLeft
            ? Visibility.Visible
            : Visibility.Collapsed;
        ScrollRightButton.Visibility = canScrollRight
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// Gets the internal ListBox control for external binding if needed.
    /// </summary>
    public ListBox TabsListBox => SessionTabs;

    /// <summary>
    /// Finds the ScrollViewer inside the ListBox's visual tree.
    /// </summary>
    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv)
                return sv;

            var result = FindScrollViewer(child);
            if (result != null)
                return result;
        }
        return null;
    }
}
