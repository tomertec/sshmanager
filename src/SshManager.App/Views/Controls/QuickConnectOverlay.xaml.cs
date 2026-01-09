using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SshManager.App.ViewModels;
using SshManager.Core.Models;

namespace SshManager.App.Views.Controls;

/// <summary>
/// Overlay control for quick host search and connection via Ctrl+K.
/// Provides a command palette style interface for rapid host selection.
/// </summary>
public partial class QuickConnectOverlay : UserControl
{
    public QuickConnectOverlay()
    {
        InitializeComponent();

        // When DataContext changes, subscribe to IsOpen changes
        DataContextChanged += OnDataContextChanged;
    }

    private QuickConnectOverlayViewModel? ViewModel => DataContext as QuickConnectOverlayViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is QuickConnectOverlayViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is QuickConnectOverlayViewModel newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuickConnectOverlayViewModel.IsOpen) && ViewModel?.IsOpen == true)
        {
            // Focus the search box when overlay opens
            Dispatcher.BeginInvoke(() =>
            {
                SearchTextBox.Focus();
                SearchTextBox.SelectAll();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// Opens the overlay and focuses the search box.
    /// </summary>
    public void Open()
    {
        ViewModel?.Open();
    }

    /// <summary>
    /// Closes the overlay.
    /// </summary>
    public void Close()
    {
        ViewModel?.Close();
    }

    private void SearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                // Select the currently highlighted host
                ViewModel?.SelectHostCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape:
                // Close the overlay
                ViewModel?.CloseCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Down:
                // Move selection down
                ViewModel?.SelectNextCommand.Execute(null);
                HostListBox.ScrollIntoView(ViewModel?.SelectedHost);
                e.Handled = true;
                break;

            case Key.Up:
                // Move selection up
                ViewModel?.SelectPreviousCommand.Execute(null);
                HostListBox.ScrollIntoView(ViewModel?.SelectedHost);
                e.Handled = true;
                break;

            case Key.Tab:
                // Tab also moves selection down (like VS Code)
                if (Keyboard.Modifiers == ModifierKeys.None)
                {
                    ViewModel?.SelectNextCommand.Execute(null);
                    HostListBox.ScrollIntoView(ViewModel?.SelectedHost);
                    e.Handled = true;
                }
                else if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    ViewModel?.SelectPreviousCommand.Execute(null);
                    HostListBox.ScrollIntoView(ViewModel?.SelectedHost);
                    e.Handled = true;
                }
                break;
        }
    }

    private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Close when clicking outside the palette
        ViewModel?.CloseCommand.Execute(null);
    }

    private void Palette_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Prevent click from propagating to overlay background
        e.Handled = true;
    }

    private void HostListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Connect on mouse click (select host)
        if (HostListBox.SelectedItem != null)
        {
            ViewModel?.SelectHostCommand.Execute(null);
        }
    }

    private void RecentHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Connect to recent host when clicked
        if (sender is FrameworkElement element && element.DataContext is HostEntry host)
        {
            ViewModel?.ConnectToHostCommand.Execute(host);
        }
    }
}
