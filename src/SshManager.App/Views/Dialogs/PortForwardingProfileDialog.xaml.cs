using System.Windows;
using SshManager.App.ViewModels;
using SshManager.Core.Models;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

public partial class PortForwardingProfileDialog : FluentWindow
{
    private readonly PortForwardingProfileDialogViewModel _viewModel;

    public PortForwardingProfileDialog(PortForwardingProfileDialogViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        _viewModel.RequestClose += OnRequestClose;
    }

    /// <summary>
    /// Loads available hosts into the dialog for association selection.
    /// </summary>
    public void LoadAvailableHosts(IEnumerable<HostEntry> hosts)
    {
        _viewModel.LoadAvailableHosts(hosts);
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
