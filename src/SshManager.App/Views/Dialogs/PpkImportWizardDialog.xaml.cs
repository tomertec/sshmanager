using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using SshManager.App.ViewModels;

namespace SshManager.App.Views.Dialogs;

/// <summary>
/// Interaction logic for PpkImportWizardDialog.
/// Multi-step wizard for importing PPK files.
/// </summary>
public partial class PpkImportWizardDialog
{
    private readonly PpkImportWizardViewModel _viewModel;
    private readonly ILogger<PpkImportWizardDialog> _logger;

    public PpkImportWizardDialog(PpkImportWizardViewModel viewModel, ILogger<PpkImportWizardDialog>? logger = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PpkImportWizardDialog>.Instance;
        DataContext = _viewModel;

        _viewModel.RequestClose += () =>
        {
            DialogResult = _viewModel.DialogResult;
            Close();
        };
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    await _viewModel.AddFiles(files);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Window_Drop");
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
