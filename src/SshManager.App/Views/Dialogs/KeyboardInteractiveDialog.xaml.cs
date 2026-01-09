using SshManager.App.ViewModels;
using SshManager.Terminal.Models;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

/// <summary>
/// Dialog for handling keyboard-interactive authentication (2FA/TOTP).
/// </summary>
public partial class KeyboardInteractiveDialog : FluentWindow
{
    private readonly KeyboardInteractiveViewModel _viewModel;

    public KeyboardInteractiveDialog()
    {
        _viewModel = new KeyboardInteractiveViewModel();
        DataContext = _viewModel;

        InitializeComponent();

        _viewModel.RequestClose += () =>
        {
            DialogResult = _viewModel.DialogResult;
            Close();
        };
    }

    /// <summary>
    /// Initializes the dialog with an authentication request.
    /// </summary>
    public void Initialize(AuthenticationRequest request)
    {
        _viewModel.Initialize(request);
    }

    /// <summary>
    /// Gets whether the user submitted responses.
    /// </summary>
    public bool IsSubmitted => _viewModel.DialogResult == true;

    /// <summary>
    /// Gets the authentication request with responses filled in.
    /// Returns null if the user cancelled.
    /// </summary>
    public AuthenticationRequest? GetResponseRequest()
    {
        if (!IsSubmitted)
        {
            return null;
        }

        return _viewModel.GetResponseRequest();
    }
}
