using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private readonly ISshKeyManager? _keyManager;

    public HostEditDialog(HostDialogViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        // Subscribe to close request
        _viewModel.RequestClose += OnRequestClose;

        // Wire up section events
        WireSectionEvents();

        // Set initial password if available
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Constructor with repository dependencies for advanced features.
    /// </summary>
    /// <param name="viewModel">The host dialog view model.</param>
    /// <param name="hostProfileRepo">The host profile repository.</param>
    /// <param name="proxyJumpRepo">The proxy jump profile repository.</param>
    /// <param name="portForwardingRepo">The port forwarding profile repository.</param>
    /// <param name="hostRepo">The host repository.</param>
    /// <param name="availableHosts">Available hosts for proxy jump configuration.</param>
    /// <param name="keyManager">The SSH key manager.</param>
    public HostEditDialog(
        HostDialogViewModel viewModel,
        IHostProfileRepository? hostProfileRepo,
        IProxyJumpProfileRepository? proxyJumpRepo,
        IPortForwardingProfileRepository? portForwardingRepo,
        IHostRepository? hostRepo,
        IReadOnlyList<HostEntry>? availableHosts = null,
        ISshKeyManager? keyManager = null)
        : this(viewModel)
    {
        _hostProfileRepo = hostProfileRepo;
        _proxyJumpRepo = proxyJumpRepo;
        _portForwardingRepo = portForwardingRepo;
        _hostRepo = hostRepo;
        _availableHosts = availableHosts;
        _keyManager = keyManager;
    }

    /// <summary>
    /// Wire up events from section controls to dialog handlers.
    /// </summary>
    private void WireSectionEvents()
    {
        // SshConnectionSection events
        SshConnectionSection.ManageHostProfilesRequested += (_, _) => ManageHostProfiles();

        // AuthenticationSection events
        AuthenticationSection.SelectKeyRequested += (_, _) => SelectKey();
        // Password is now in SshSettings child ViewModel
        AuthenticationSection.PasswordChanged += (_, password) => _viewModel.SshSettings.Password = password;

        // AdvancedOptionsSection events
        AdvancedOptionsSection.ManageProxyJumpProfilesRequested += (_, _) => ManageProxyJumpProfiles();
        AdvancedOptionsSection.ManagePortForwardingRequested += (_, _) => ManagePortForwarding();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Set the password in the AuthenticationSection from SshSettings child ViewModel
        if (!string.IsNullOrEmpty(_viewModel.SshSettings.Password))
        {
            AuthenticationSection.SetPassword(_viewModel.SshSettings.Password);
        }

        // Note: Data loading is now handled by HostManagementViewModel calling LoadDataAsync()
        // before showing the dialog, so we don't need to load here anymore.
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

    #region Section Event Handlers

    private async void SelectKey()
    {
        try
        {
            if (_keyManager == null)
            {
                System.Windows.MessageBox.Show(
                    "SSH key manager is not available.",
                    "Feature Not Available",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            var keys = await _keyManager.GetExistingKeysAsync();

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
                // PrivateKeyPath is now in SshSettings child ViewModel
                _viewModel.SshSettings.PrivateKeyPath = dialog.SelectedKey.PrivateKeyPath;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in SelectKey: {ex}");
        }
    }

    private async void ManageHostProfiles()
    {
        try
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

            // Use null logger if view model doesn't expose a logger factory
            var managerVm = new HostProfileManagerViewModel(_hostProfileRepo, _proxyJumpRepo, null);
            var dialog = new HostProfileManagerDialog
            {
                DataContext = managerVm,
                Owner = this
            };

            managerVm.RequestClose += () => dialog.Close();
            await managerVm.LoadProfilesAsync();

            dialog.ShowDialog();

            // Reload profiles after management via the SshSettings child ViewModel
            await _viewModel.SshSettings.LoadHostProfilesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in ManageHostProfiles: {ex}");
        }
    }

    private async void ManageProxyJumpProfiles()
    {
        try
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

            // Reload profiles after management via the SshSettings child ViewModel
            await _viewModel.SshSettings.LoadProxyJumpProfilesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in ManageProxyJumpProfiles: {ex}");
        }
    }

    private async void ManagePortForwarding()
    {
        try
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

            // Reload count after management via the SshSettings child ViewModel
            await _viewModel.SshSettings.LoadPortForwardingCountAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in ManagePortForwarding: {ex}");
        }
    }

    #endregion
}
