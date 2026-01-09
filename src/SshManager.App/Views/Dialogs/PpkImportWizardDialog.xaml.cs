using System.Windows;
using System.Windows.Controls;
using SshManager.App.ViewModels;

namespace SshManager.App.Views.Dialogs;

/// <summary>
/// Interaction logic for PpkImportWizardDialog.
/// Multi-step wizard for importing PPK files.
/// </summary>
public partial class PpkImportWizardDialog
{
    private readonly PpkImportWizardViewModel _viewModel;

    public PpkImportWizardDialog(PpkImportWizardViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        _viewModel.RequestClose += () =>
        {
            DialogResult = _viewModel.DialogResult;
            Close();
        };
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                _viewModel.AddFiles(files);
            }
        }
    }

    private void PassphraseBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox && passwordBox.Tag is PpkImportItem item)
        {
            item.Passphrase = passwordBox.Password;
        }
    }

    private void NewPassphraseBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox)
        {
            _viewModel.NewPassphrase = passwordBox.Password;
        }
    }
}
