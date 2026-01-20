using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

public partial class KeyboardShortcutsDialog : FluentWindow
{
    public KeyboardShortcutsDialog()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Close();
    }
}
