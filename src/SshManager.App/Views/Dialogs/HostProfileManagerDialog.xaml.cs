using System.Windows.Input;
using SshManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

/// <summary>
/// Dialog for managing host profiles.
/// </summary>
public partial class HostProfileManagerDialog : FluentWindow
{
    public HostProfileManagerDialog()
    {
        InitializeComponent();
    }

    private void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is HostProfileManagerViewModel viewModel)
        {
            if (viewModel.EditProfileCommand.CanExecute(null))
            {
                viewModel.EditProfileCommand.Execute(null);
            }
        }
    }
}
