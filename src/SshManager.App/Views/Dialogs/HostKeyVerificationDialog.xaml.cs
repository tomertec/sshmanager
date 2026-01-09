using SshManager.App.ViewModels;
using SshManager.Core.Models;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

/// <summary>
/// Dialog for verifying SSH host key fingerprints.
/// </summary>
public partial class HostKeyVerificationDialog : FluentWindow
{
    private readonly HostKeyVerificationViewModel _viewModel;

    public HostKeyVerificationDialog()
    {
        _viewModel = new HostKeyVerificationViewModel();
        DataContext = _viewModel;

        InitializeComponent();

        _viewModel.RequestClose += () =>
        {
            DialogResult = _viewModel.DialogResult;
            Close();
        };
    }

    /// <summary>
    /// Initializes the dialog with host key information.
    /// </summary>
    public void Initialize(
        string hostname,
        int port,
        string algorithm,
        string fingerprint,
        HostFingerprint? existingFingerprint)
    {
        _viewModel.Initialize(hostname, port, algorithm, fingerprint, existingFingerprint);
    }

    /// <summary>
    /// Gets whether the user accepted the fingerprint.
    /// </summary>
    public bool IsAccepted => _viewModel.DialogResult == true;
}
