using System.Windows;
using System.Windows.Controls;
using SshManager.App.Models;
using SshManager.Core.Models;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Controls;

/// <summary>
/// Panel containing the host list, search, tags, groups filter, and action buttons.
/// This is the main left panel of the application.
/// </summary>
public partial class HostListPanel : UserControl
{
    /// <summary>
    /// Event raised when the settings button is clicked.
    /// </summary>
    public event EventHandler? SettingsRequested;

    /// <summary>
    /// Event raised when the quick connect overlay button is clicked.
    /// </summary>
    public event EventHandler? QuickConnectOverlayRequested;

    /// <summary>
    /// Event raised when keyboard shortcuts help is requested.
    /// </summary>
    public event EventHandler? KeyboardShortcutsRequested;

    /// <summary>
    /// Event raised when the about dialog is requested.
    /// </summary>
    public event EventHandler? AboutRequested;

    /// <summary>
    /// Event raised when the history button is clicked.
    /// </summary>
    public event EventHandler? HistoryRequested;

    /// <summary>
    /// Event raised when the snippets button is clicked.
    /// </summary>
    public event EventHandler? SnippetsRequested;

    /// <summary>
    /// Event raised when the key manager button is clicked.
    /// </summary>
    public event EventHandler? KeyManagerRequested;

    /// <summary>
    /// Event raised when the recordings button is clicked.
    /// </summary>
    public event EventHandler? RecordingsRequested;

    /// <summary>
    /// Event raised when the serial quick connect button is clicked.
    /// </summary>
    public event EventHandler? SerialQuickConnectRequested;

    public HostListPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets the SearchBox control for external focus management.
    /// </summary>
    public Wpf.Ui.Controls.TextBox SearchBoxControl => SearchBox;

    /// <summary>
    /// Gets the GroupFilterMenu control for external population.
    /// </summary>
    public ContextMenu GroupFilterMenuControl => GroupFilterMenu;

    /// <summary>
    /// Gets the GroupFilterText TextBlock for external updates.
    /// </summary>
    public System.Windows.Controls.TextBlock GroupFilterTextBlock => GroupFilterText;

    /// <summary>
    /// Gets the HostListBox control for external access.
    /// </summary>
    public ListBox HostListBoxControl => HostListBox;

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void QuickConnectOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        QuickConnectOverlayRequested?.Invoke(this, EventArgs.Empty);
    }

    private void KeyboardShortcutsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        KeyboardShortcutsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        AboutRequested?.Invoke(this, EventArgs.Empty);
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        HistoryRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SnippetsButton_Click(object sender, RoutedEventArgs e)
    {
        SnippetsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void KeyManagerButton_Click(object sender, RoutedEventArgs e)
    {
        KeyManagerRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RecordingsButton_Click(object sender, RoutedEventArgs e)
    {
        RecordingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SerialQuickConnectButton_Click(object sender, RoutedEventArgs e)
    {
        SerialQuickConnectRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Updates the group filter menu with the specified items.
    /// Called from MainWindow when groups are refreshed.
    /// </summary>
    public void UpdateGroupFilterMenu(IEnumerable<GroupFilterItem> items, HostGroup? selectedGroupFilter, Action<GroupFilterItem> onItemClick)
    {
        // Find the separator in the menu (after management items)
        var menu = GroupFilterMenu;
        if (menu == null) return;

        // Remove all items after the separator (dynamically added group items)
        var separatorIndex = -1;
        for (int i = 0; i < menu.Items.Count; i++)
        {
            if (menu.Items[i] is Separator)
            {
                separatorIndex = i;
                break;
            }
        }

        if (separatorIndex >= 0)
        {
            // Remove items after separator
            while (menu.Items.Count > separatorIndex + 1)
            {
                menu.Items.RemoveAt(menu.Items.Count - 1);
            }
        }

        // Add group filter items
        foreach (var item in items)
        {
            var menuItem = new Wpf.Ui.Controls.MenuItem
            {
                Header = item.HasCount ? $"{item.Name} ({item.Count})" : item.Name,
                Tag = item,
                FontWeight = item.Group == null ? FontWeights.SemiBold : FontWeights.Normal
            };

            // Add icon based on group
            if (item.Group == null)
            {
                menuItem.Icon = new SymbolIcon { Symbol = SymbolRegular.Grid24 };
            }
            else
            {
                menuItem.Icon = new SymbolIcon { Symbol = SymbolRegular.Folder24 };
            }

            // Mark the selected item
            if ((selectedGroupFilter == null && item.Group == null) ||
                (selectedGroupFilter != null && item.Group?.Id == selectedGroupFilter.Id))
            {
                menuItem.Icon = new SymbolIcon { Symbol = SymbolRegular.Checkmark24 };
            }

            menuItem.Click += (s, e) => onItemClick(item);
            menu.Items.Add(menuItem);
        }
    }

    /// <summary>
    /// Updates the group filter button text to show the current selection.
    /// </summary>
    public void UpdateGroupFilterButtonText(string text)
    {
        if (GroupFilterText != null)
        {
            GroupFilterText.Text = text;
        }
    }
}
