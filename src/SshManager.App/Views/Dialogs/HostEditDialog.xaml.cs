using System.Windows;
using System.Windows.Controls;
using SshManager.App.ViewModels;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Security;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

public partial class HostEditDialog : FluentWindow
{
    private readonly HostDialogViewModel _viewModel;
    private readonly IHostProfileRepository? _hostProfileRepo;
    private readonly IProxyJumpProfileRepository? _proxyJumpRepo;
    private readonly IPortForwardingProfileRepository? _portForwardingRepo;
    private readonly IHostRepository? _hostRepo;
    private readonly IReadOnlyList<HostEntry>? _availableHosts;

    public HostEditDialog(HostDialogViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        // Subscribe to close request
        _viewModel.RequestClose += OnRequestClose;

        // Set initial password if available
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Constructor with repository dependencies for advanced features.
    /// </summary>
    public HostEditDialog(
        HostDialogViewModel viewModel,
        IHostProfileRepository? hostProfileRepo,
        IProxyJumpProfileRepository? proxyJumpRepo,
        IPortForwardingProfileRepository? portForwardingRepo,
        IHostRepository? hostRepo,
        IReadOnlyList<HostEntry>? availableHosts = null)
        : this(viewModel)
    {
        _hostProfileRepo = hostProfileRepo;
        _proxyJumpRepo = proxyJumpRepo;
        _portForwardingRepo = portForwardingRepo;
        _hostRepo = hostRepo;
        _availableHosts = availableHosts;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Set the password box content (can't bind to PasswordBox.Password directly)
        if (!string.IsNullOrEmpty(_viewModel.Password))
        {
            PasswordBox.Password = _viewModel.Password;
        }

        // Load host profiles, ProxyJump profiles and port forwarding count
        await _viewModel.LoadHostProfilesAsync();
        await _viewModel.LoadProxyJumpProfilesAsync();
        await _viewModel.LoadPortForwardingCountAsync();

        // Initialize SSH agent status check if using SSH Agent auth
        await _viewModel.InitializeAgentStatusAsync();
    }

    private void OnRequestClose()
    {
        DialogResult = _viewModel.DialogResult;
        Close();
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.PasswordBox passwordBox)
        {
            _viewModel.Password = passwordBox.Password;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.RequestClose -= OnRequestClose;
        base.OnClosed(e);
    }

    private async void SelectKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var keyManager = App.GetService<ISshKeyManager>();
        var keys = await keyManager.GetExistingKeysAsync();

        if (keys.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "No SSH keys found in your ~/.ssh directory.\n\nYou can generate a new key using the SSH Key Manager (Ctrl+K).",
                "No Keys Found",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        // Create a simple selection dialog
        var dialog = new KeySelectionDialog(keys)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.SelectedKey != null)
        {
            _viewModel.PrivateKeyPath = dialog.SelectedKey.PrivateKeyPath;
        }
    }

    private async void ManageProxyJumpProfiles_Click(object sender, RoutedEventArgs e)
    {
        if (_proxyJumpRepo == null || _hostRepo == null)
        {
            System.Windows.MessageBox.Show(
                "ProxyJump profile management is not available.\n\nPlease ensure repositories are properly configured.",
                "Feature Not Available",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        // Get available hosts for the profile editor
        var hosts = _availableHosts ?? await _hostRepo.GetAllAsync();

        var profileVm = new ProxyJumpProfileDialogViewModel(_proxyJumpRepo, _hostRepo);
        var dialog = new ProxyJumpProfileDialog(profileVm)
        {
            Owner = this
        };

        dialog.ShowDialog();

        // Reload profiles after management
        await _viewModel.LoadProxyJumpProfilesAsync();
    }

    private async void ManagePortForwarding_Click(object sender, RoutedEventArgs e)
    {
        if (_portForwardingRepo == null || _hostRepo == null)
        {
            System.Windows.MessageBox.Show(
                "Port forwarding management is not available.\n\nPlease ensure repositories are properly configured.",
                "Feature Not Available",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        // Get available hosts for the profile editor
        var hosts = _availableHosts ?? await _hostRepo.GetAllAsync();

        var managerVm = new PortForwardingManagerViewModel(_portForwardingRepo);
        var dialog = new PortForwardingListDialog(managerVm, _portForwardingRepo, hosts)
        {
            Owner = this
        };

        dialog.ShowDialog();

        // Reload count after management
        await _viewModel.LoadPortForwardingCountAsync();
    }

    private async void ManageHostProfiles_Click(object sender, RoutedEventArgs e)
    {
        if (_hostProfileRepo == null || _proxyJumpRepo == null)
        {
            System.Windows.MessageBox.Show(
                "Host profile management is not available.\n\nPlease ensure repositories are properly configured.",
                "Feature Not Available",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var logger = App.GetService<Microsoft.Extensions.Logging.ILogger<HostProfileManagerViewModel>>();
        var managerVm = new HostProfileManagerViewModel(_hostProfileRepo, _proxyJumpRepo, logger);
        var dialog = new HostProfileManagerDialog
        {
            DataContext = managerVm,
            Owner = this
        };

        managerVm.RequestClose += () => dialog.Close();
        await managerVm.LoadProfilesAsync();

        dialog.ShowDialog();

        // Reload profiles after management
        await _viewModel.LoadHostProfilesAsync();
    }
}
