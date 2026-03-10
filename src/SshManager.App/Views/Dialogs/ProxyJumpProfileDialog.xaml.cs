using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

public partial class ProxyJumpProfileDialog : FluentWindow
{
    private readonly ProxyJumpProfileDialogViewModel _viewModel;
    private readonly ILogger<ProxyJumpProfileDialog> _logger;

    public ProxyJumpProfileDialog(ProxyJumpProfileDialogViewModel viewModel, ILogger<ProxyJumpProfileDialog>? logger = null)
    {
        _viewModel = viewModel;
        _logger = logger ?? NullLogger<ProxyJumpProfileDialog>.Instance;
        DataContext = viewModel;

        InitializeComponent();

        _viewModel.RequestClose += OnRequestClose;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.LoadAvailableHostsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ProxyJumpProfileDialog.OnLoaded");
        }
    }

    private void OnRequestClose()
    {
        DialogResult = _viewModel.DialogResult;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.RequestClose -= OnRequestClose;
        base.OnClosed(e);
    }
}
