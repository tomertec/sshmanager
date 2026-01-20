using System.Windows;
using System.Windows.Controls;
using SshManager.Terminal;

namespace SshManager.App.Views.Controls;

/// <summary>
/// A horizontal strip of session tabs for switching between terminal sessions.
/// Supports broadcast mode indicators and group color coding.
/// </summary>
public partial class SessionTabStrip : UserControl
{
    /// <summary>
    /// Event raised when the selected session tab changes.
    /// </summary>
    public event EventHandler<TerminalSession?>? SessionSelectionChanged;

    public SessionTabStrip()
    {
        InitializeComponent();
    }

    private void SessionTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem is TerminalSession session)
        {
            SessionSelectionChanged?.Invoke(this, session);
        }
        else
        {
            SessionSelectionChanged?.Invoke(this, null);
        }
    }

    /// <summary>
    /// Gets the internal ListBox control for external binding if needed.
    /// </summary>
    public ListBox TabsListBox => SessionTabs;
}
