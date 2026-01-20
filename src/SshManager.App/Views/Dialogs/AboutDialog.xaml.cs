using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

public partial class AboutDialog : FluentWindow
{
    public AboutDialog()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Close();
    }
}
