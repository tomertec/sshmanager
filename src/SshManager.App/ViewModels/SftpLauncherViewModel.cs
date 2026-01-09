using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Security;
using SshManager.Terminal.Models;
using SshManager.Terminal.Services;
using SshManager.App.Views.Windows;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel responsible for launching SFTP browser windows.
/// Handles SFTP connections for both active sessions and direct host connections.
/// </summary>
public partial class SftpLauncherViewModel : ObservableObject, IDisposable
{
    private readonly SessionViewModel _sessionViewModel;
    private readonly ISftpService _sftpService;
    private readonly ISettingsRepository _settingsRepo;
    private readonly ISecretProtector _secretProtector;
    private readonly ILogger<SftpLauncherViewModel> _logger;

    public SftpLauncherViewModel(
        SessionViewModel sessionViewModel,
        ISftpService sftpService,
        ISettingsRepository settingsRepo,
        ISecretProtector secretProtector,
        ILogger<SftpLauncherViewModel>? logger = null)
    {
        _sessionViewModel = sessionViewModel;
        _sftpService = sftpService;
        _settingsRepo = settingsRepo;
        _secretProtector = secretProtector;
        _logger = logger ?? NullLogger<SftpLauncherViewModel>.Instance;

        // Subscribe to CurrentSession changes to update command availability
        _sessionViewModel.PropertyChanged += OnSessionViewModelPropertyChanged;

        _logger.LogDebug("SftpLauncherViewModel initialized");
    }

    private void OnSessionViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionViewModel.CurrentSession))
        {
            OpenSftpBrowserCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Opens SFTP browser in a separate window for the current session's host.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenSftpBrowser))]
    private async Task OpenSftpBrowserAsync()
    {
        var session = _sessionViewModel.CurrentSession;
        if (session?.Host == null) return;

        var host = session.Host;
        _logger.LogInformation("Opening SFTP browser window for host {DisplayName}", host.DisplayName);

        try
        {
            // Get password from session if available (convert SecureString to string for API)
            string? password = session.DecryptedPassword?.ToUnsecureString();

            // Create connection info
            var connectionInfo = await _sessionViewModel.CreateConnectionInfoAsync(host, password);

            // Connect SFTP
            var sftpSession = await _sftpService.ConnectAsync(connectionInfo);

            // Create ViewModels
            var sftpBrowserVm = new SftpBrowserViewModel(
                sftpSession,
                host.DisplayName,
                App.GetLogger<SftpBrowserViewModel>(),
                App.GetLogger<LocalFileBrowserViewModel>(),
                App.GetLogger<RemoteFileBrowserViewModel>());

            // Initialize the browsers
            await sftpBrowserVm.InitializeAsync();

            var windowVm = new SftpBrowserWindowViewModel(sftpBrowserVm, host.DisplayName);

            // Create and show window (no Owner so user can freely switch between windows)
            var window = new SftpBrowserWindow(windowVm);
            window.Show();

            _logger.LogInformation("SFTP browser window opened for {DisplayName}", host.DisplayName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open SFTP browser for host {DisplayName}", host.DisplayName);

            var messageBox = new Wpf.Ui.Controls.MessageBox
            {
                Title = "SFTP Connection Failed",
                Content = $"Could not connect to SFTP:\n\n{ex.Message}"
            };
            await messageBox.ShowDialogAsync();
        }
    }

    private bool CanOpenSftpBrowser() => _sessionViewModel.CurrentSession?.Host != null;

    /// <summary>
    /// Opens SFTP browser directly for a host from the host list (without requiring an active session).
    /// </summary>
    [RelayCommand]
    private async Task OpenSftpBrowserForHostAsync(HostEntry? host)
    {
        if (host == null) return;

        _logger.LogInformation("Opening SFTP browser window for host {DisplayName} from host list", host.DisplayName);

        try
        {
            // Get password - check cache first, then fall back to stored password
            string? password = null;
            var settings = await _settingsRepo.GetAsync();

            if (host.AuthType == AuthType.Password)
            {
                // Try to get cached credential first
                if (settings.EnableCredentialCaching)
                {
                    var cachedCredential = _sessionViewModel.CredentialCache.GetCachedCredential(host.Id);
                    if (cachedCredential != null && cachedCredential.Type == CredentialType.Password)
                    {
                        password = cachedCredential.GetValue();
                        _logger.LogDebug("Using cached password for SFTP to host {DisplayName}", host.DisplayName);
                    }
                }

                // Fall back to stored password if no cached credential
                if (password == null && !string.IsNullOrEmpty(host.PasswordProtected))
                {
                    password = _secretProtector.TryUnprotect(host.PasswordProtected);
                    if (password == null)
                    {
                        _logger.LogWarning("Failed to decrypt password for SFTP to host {DisplayName}", host.DisplayName);
                    }
                    else
                    {
                        _logger.LogDebug("Password decrypted successfully for SFTP to host {DisplayName}", host.DisplayName);

                        // Cache the credential for future connections if caching is enabled
                        if (settings.EnableCredentialCaching)
                        {
                            _sessionViewModel.CacheCredentialForHost(host.Id, password, CredentialType.Password);
                        }
                    }
                }
            }

            // Create connection info
            var connectionInfo = await _sessionViewModel.CreateConnectionInfoAsync(host, password);

            // Connect SFTP
            var sftpSession = await _sftpService.ConnectAsync(connectionInfo);

            // Create ViewModels
            var sftpBrowserVm = new SftpBrowserViewModel(
                sftpSession,
                host.DisplayName,
                App.GetLogger<SftpBrowserViewModel>(),
                App.GetLogger<LocalFileBrowserViewModel>(),
                App.GetLogger<RemoteFileBrowserViewModel>());

            // Initialize the browsers
            await sftpBrowserVm.InitializeAsync();

            var windowVm = new SftpBrowserWindowViewModel(sftpBrowserVm, host.DisplayName);

            // Create and show window (no Owner so user can freely switch between windows)
            var window = new SftpBrowserWindow(windowVm);
            window.Show();

            _logger.LogInformation("SFTP browser window opened for {DisplayName} from host list", host.DisplayName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open SFTP browser for host {DisplayName}", host.DisplayName);

            var messageBox = new Wpf.Ui.Controls.MessageBox
            {
                Title = "SFTP Connection Failed",
                Content = $"Could not connect to SFTP:\n\n{ex.Message}"
            };
            await messageBox.ShowDialogAsync();
        }
    }

    public void Dispose()
    {
        _sessionViewModel.PropertyChanged -= OnSessionViewModelPropertyChanged;
    }
}
