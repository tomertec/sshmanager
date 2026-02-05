using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.App.Services;
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
    private readonly IEditorThemeService _editorThemeService;
    private readonly ILogger<SftpLauncherViewModel> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public SftpLauncherViewModel(
        SessionViewModel sessionViewModel,
        ISftpService sftpService,
        ISettingsRepository settingsRepo,
        ISecretProtector secretProtector,
        IEditorThemeService editorThemeService,
        ILogger<SftpLauncherViewModel>? logger = null,
        ILoggerFactory? loggerFactory = null)
    {
        _sessionViewModel = sessionViewModel;
        _sftpService = sftpService;
        _settingsRepo = settingsRepo;
        _secretProtector = secretProtector;
        _editorThemeService = editorThemeService;
        _logger = logger ?? NullLogger<SftpLauncherViewModel>.Instance;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;

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
        // SECURITY NOTE: Converting SecureString to a managed string defeats the purpose of SecureString,
        // as the plaintext remains in memory until GC. Consider passing SecureString through to SSH.NET
        // or using a pinned byte array that can be explicitly zeroed after use.
        string? password = session.DecryptedPassword?.ToUnsecureString();

        var connectionInfo = await _sessionViewModel.CreateConnectionInfoAsync(host, password);
        await LaunchSftpWindowAsync(connectionInfo, host.DisplayName);
    }

    private bool CanOpenSftpBrowser() => _sessionViewModel.CurrentSession?.Host != null;

    /// <summary>
    /// Opens SFTP browser directly for a host from the host list (without requiring an active session).
    /// </summary>
    [RelayCommand]
    private async Task OpenSftpBrowserForHostAsync(HostEntry? host)
    {
        if (host == null) return;

        string? password = await ResolvePasswordForHostAsync(host);
        var connectionInfo = await _sessionViewModel.CreateConnectionInfoAsync(host, password);
        await LaunchSftpWindowAsync(connectionInfo, host.DisplayName);
    }

    /// <summary>
    /// Resolves the password for a host entry, checking cache first then stored credentials.
    /// </summary>
    private async Task<string?> ResolvePasswordForHostAsync(HostEntry host)
    {
        if (host.AuthType != AuthType.Password) return null;

        var settings = await _settingsRepo.GetAsync();

        // Try cached credential first
        if (settings.EnableCredentialCaching)
        {
            var cachedCredential = _sessionViewModel.CredentialCache.GetCachedCredential(host.Id);
            if (cachedCredential != null && cachedCredential.Type == CredentialType.Password)
            {
                _logger.LogDebug("Using cached password for SFTP to host {DisplayName}", host.DisplayName);
                return cachedCredential.GetValue();
            }
        }

        // Fall back to stored password
        if (string.IsNullOrEmpty(host.PasswordProtected)) return null;

        var password = _secretProtector.TryUnprotect(host.PasswordProtected);
        if (password == null)
        {
            _logger.LogWarning("Failed to decrypt password for SFTP to host {DisplayName}", host.DisplayName);
            return null;
        }

        // Cache for future connections if enabled
        if (settings.EnableCredentialCaching)
        {
            _sessionViewModel.CacheCredentialForHost(host.Id, password, CredentialType.Password);
        }

        return password;
    }

    /// <summary>
    /// Shared logic for connecting SFTP, creating the browser VM, and showing the window.
    /// </summary>
    private async Task LaunchSftpWindowAsync(TerminalConnectionInfo connectionInfo, string displayName)
    {
        _logger.LogInformation("Opening SFTP browser window for {DisplayName}", displayName);

        try
        {
            var sftpSession = await _sftpService.ConnectAsync(connectionInfo);

            var sftpBrowserVm = new SftpBrowserViewModel(
                sftpSession,
                displayName,
                _editorThemeService,
                _loggerFactory);

            await sftpBrowserVm.InitializeAsync();

            // Wire up settings persistence
            var settings = await _settingsRepo.GetAsync();
            sftpBrowserVm.SetSettingsCallbacks(
                () => settings.SftpMirrorNavigation,
                value => { settings.SftpMirrorNavigation = value; _ = _settingsRepo.UpdateAsync(settings).ContinueWith(t =>
                    System.Diagnostics.Debug.WriteLine($"Settings save error: {t.Exception}"),
                    TaskContinuationOptions.OnlyOnFaulted); },
                () => settings.SftpFavorites ?? "",
                value => { settings.SftpFavorites = value; _ = _settingsRepo.UpdateAsync(settings).ContinueWith(t =>
                    System.Diagnostics.Debug.WriteLine($"Settings save error: {t.Exception}"),
                    TaskContinuationOptions.OnlyOnFaulted); });

            var windowVm = new SftpBrowserWindowViewModel(sftpBrowserVm, displayName);
            var window = new SftpBrowserWindow(windowVm);
            window.Show();

            _logger.LogInformation("SFTP browser window opened for {DisplayName}", displayName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open SFTP browser for {DisplayName}", displayName);

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
