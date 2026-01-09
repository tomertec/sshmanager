using System.Windows;
using SshManager.Security;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

public partial class KeySelectionDialog : FluentWindow
{
    public SshKeyInfo? SelectedKey { get; private set; }

    public KeySelectionDialog(IReadOnlyList<SshKeyInfo> keys)
    {
        InitializeComponent();
        KeyList.ItemsSource = keys;

        if (keys.Count > 0)
        {
            KeyList.SelectedIndex = 0;
        }
    }

    private void SelectButton_Click(object sender, RoutedEventArgs e)
    {
        if (KeyList.SelectedItem is SshKeyInfo key)
        {
            SelectedKey = key;
            DialogResult = true;
            Close();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
