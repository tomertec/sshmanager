using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Security;
using SshManager.Terminal;
using SshManager.Terminal.Models;
using SshManager.Terminal.Services;
using SshManager.App.Services;
using System.Collections.Concurrent;
using SshManager.App.Views.Dialogs;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel responsible for terminal session lifecycle and SSH connections.
/// Handles session creation, connection management, and authentication callbacks.
/// </summary>
public partial class SessionViewModel : ObservableObject, IDisposable
{
    private readonly ITerminalSessionManager _sessionManager;
    private readonly ISshConnectionService _sshService;
    private readonly ISerialConnectionService _serialConnectionService;
    private readonly IConnectionHistoryRepository _historyRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly ISecretProtector _secretProtector;
    private readonly ICredentialCache _credentialCache;
    private readonly IHostFingerprintRepository _fingerprintRepo;
    private readonly ISessionLoggingService _sessionLoggingService;
    private readonly IExternalTerminalService _externalTerminalService;
    private readonly ILogger<SessionViewModel> _logger;

    /// <summary>
    /// Per-host semaphores for synchronizing connection attempts.
    /// Allows concurrent connections to different hosts while preventing duplicate attempts per host.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _hostConnectionLocks = new();

    /// <summary>
    /// Tracks hosts currently being connected, mapping hostId to display name.
    /// Serves as both duplicate-connection guard and progress UI state source.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, string> _connectingHosts = new();

    [ObservableProperty]
    private TerminalSession? _currentSession;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private string? _connectingHostName;

    private SemaphoreSlim GetHostConnectionLock(Guid hostId) =>
        _hostConnectionLocks.GetOrAdd(hostId, _ => new SemaphoreSlim(1, 1));

    private void AddConnectingHost(HostEntry host)
    {
        _connectingHosts[host.Id] = host.DisplayName;
        UpdateConnectionProgressState();
    }

    private void RemoveConnectingHost(Guid hostId)
    {
        _connectingHosts.TryRemove(hostId, out _);
        UpdateConnectionProgressState();
        TryCleanupHostConnectionLock(hostId);
    }

    /// <summary>
    /// Attempts to remove and dispose the per-host semaphore if no one is waiting on it.
    /// </summary>
    private void TryCleanupHostConnectionLock(Guid hostId)
    {
        if (_hostConnectionLocks.TryRemove(hostId, out var semaphore))
        {
            if (semaphore.CurrentCount < 1)
            {
                // Someone is still holding or waiting on the semaphore; put it back.
                _hostConnectionLocks.TryAdd(hostId, semaphore);
            }
            else
            {
                semaphore.Dispose();
            }
        }
    }

    private void UpdateConnectionProgressState()
    {
        var activeCount = _connectingHosts.Count;
        IsConnecting = activeCount > 0;

        if (activeCount == 0)
        {
            ConnectingHostName = null;
            return;
        }

        var currentHost = _connectingHosts.Values.FirstOrDefault() ?? "Host";
        ConnectingHostName = activeCount == 1
            ? currentHost
            : $"{currentHost} (+{activeCount - 1} more)";
    }

    /// <summary>
    /// Event raised when a new session is created (for pane management).
    /// </summary>
    public event EventHandler<TerminalSession>? SessionCreated;

    public SessionViewModel(
        ITerminalSessionManager sessionManager,
        ISshConnectionService sshService,
        ISerialConnectionService serialConnectionService,
        IConnectionHistoryRepository historyRepo,
        ISettingsRepository settingsRepo,
        ISecretProtector secretProtector,
        ICredentialCache credentialCache,
        IHostFingerprintRepository fingerprintRepo,
        ISessionLoggingService sessionLoggingService,
        IExternalTerminalService externalTerminalService,
        ILogger<SessionViewModel>? logger = null)
    {
        _sessionManager = sessionManager;
        _sshService = sshService;
        _serialConnectionService = serialConnectionService;
        _historyRepo = historyRepo;
        _settingsRepo = settingsRepo;
        _secretProtector = secretProtector;
        _credentialCache = credentialCache;
        _fingerprintRepo = fingerprintRepo;
        _sessionLoggingService = sessionLoggingService;
        _externalTerminalService = externalTerminalService;
        _logger = logger ?? NullLogger<SessionViewModel>.Instance;

        // Subscribe to session events
        _sessionManager.SessionCreated += OnSessionCreated;
        _sessionManager.SessionClosed += OnSessionClosed;
        _sessionManager.CurrentSessionChanged += OnCurrentSessionChanged;

        _logger.LogDebug("SessionViewModel initialized");
    }

    private void OnSessionCreated(object? sender, TerminalSession session)
    {
        OnPropertyChanged(nameof(HasActiveSessions));
        OnPropertyChanged(nameof(Sessions));
    }

    private void OnSessionClosed(object? sender, TerminalSession session)
    {
        OnPropertyChanged(nameof(HasActiveSessions));
        OnPropertyChanged(nameof(Sessions));
    }

    private void OnCurrentSessionChanged(object? sender, TerminalSession? session)
    {
        CurrentSession = session;
    }

    /// <summary>
    /// Gets the collection of active terminal sessions.
    /// </summary>
    public ObservableCollection<TerminalSession> Sessions => _sessionManager.Sessions;

    /// <summary>
    /// Gets whether there are any active sessions.
    /// </summary>
    public bool HasActiveSessions => Sessions.Count > 0;

    /// <summary>
    /// Gets the SSH connection service for external access (e.g., from terminal controls).
    /// </summary>
    public ISshConnectionService SshService => _sshService;

    /// <summary>
    /// Gets the serial connection service for external access (e.g., from terminal controls).
    /// </summary>
    public ISerialConnectionService SerialConnectionService => _serialConnectionService;

    /// <summary>
    /// Gets the host fingerprint repository for external access.
    /// </summary>
    public IHostFingerprintRepository FingerprintRepository => _fingerprintRepo;

    /// <summary>
    /// Gets the credential cache for external access (e.g., from terminal controls).
    /// </summary>
    public ICredentialCache CredentialCache => _credentialCache;

    [RelayCommand]
    private async Task ConnectAsync(HostEntry? host)
    {
        if (host == null) return;

        // Quick rejection if already connecting
        if (_connectingHosts.ContainsKey(host.Id))
        {
            _logger.LogWarning("Connection to {DisplayName} already in progress, ignoring duplicate request", host.DisplayName);
            return;
        }

        var hostConnectionLock = GetHostConnectionLock(host.Id);

        // Acquire per-host connection lock to prevent duplicate attempts for this host
        if (!await hostConnectionLock.WaitAsync(TimeSpan.FromSeconds(Constants.ConnectionDefaults.ConnectionLockTimeoutSeconds)))
        {
            _logger.LogWarning("Failed to acquire host connection lock for {DisplayName} within {Timeout}s timeout", host.DisplayName, Constants.ConnectionDefaults.ConnectionLockTimeoutSeconds);
            return;
        }

        try
        {
            // Double-check inside semaphore to prevent race condition
            if (!_connectingHosts.TryAdd(host.Id, host.DisplayName))
            {
                _logger.LogWarning("Connection to {DisplayName} already in progress (race condition detected), aborting", host.DisplayName);
                return;
            }

            _logger.LogInformation("Initiating connection to {DisplayName} ({Hostname}:{Port}) using {AuthType}",
                host.DisplayName, host.Hostname, host.Port, host.AuthType);

            UpdateConnectionProgressState();

            try
            {
                var settings = await _settingsRepo.GetAsync();

                // Check if external terminal mode is enabled (only for SSH connections)
                if (!settings.UseEmbeddedTerminal && host.ConnectionType == ConnectionType.Ssh)
                {
                    _logger.LogInformation("Using external terminal for {DisplayName}", host.DisplayName);

                    // Get password for password auth (note: SSH prompts interactively, this is just for logging)
                    string? password = GetPasswordForHost(host, settings);

                    var launched = await _externalTerminalService.LaunchSshConnectionAsync(host, password);

                    if (launched)
                    {
                        // Record connection attempt in history (we can't know if it succeeded)
                        await RecordConnectionResultAsync(host, true, null);
                        _logger.LogInformation("External terminal launched for {DisplayName}", host.DisplayName);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to launch external terminal for {DisplayName}", host.DisplayName);
                    }

                    return;
                }

                // Embedded terminal mode (default behavior)
                // Get password - check cache first, then fall back to stored password
                string? embeddedPassword = GetPasswordForHost(host, settings);
                bool usedCachedCredential = embeddedPassword != null && host.AuthType == AuthType.Password &&
                                             settings.EnableCredentialCaching &&
                                             _credentialCache.GetCachedCredential(host.Id) != null;

                // Create session (TerminalSessionManager modifies ObservableCollection, protect with lock)
                var session = _sessionManager.CreateSession(host.DisplayName);
                session.Host = host;
                session.DecryptedPassword = embeddedPassword?.ToSecureString();

                // Notify that Sessions changed now that Host is set (for active session indicators)
                OnPropertyChanged(nameof(Sessions));

                _logger.LogDebug("Terminal session {SessionId} created for host {DisplayName} (cached credential: {UsedCache})",
                    session.Id, host.DisplayName, usedCachedCredential);

                // Initialize session logging if enabled
                InitializeSessionLogging(session, host, settings);

                // Raise SessionCreated event for pane management
                SessionCreated?.Invoke(this, session);
            }
            finally
            {
                RemoveConnectingHost(host.Id);
            }
        }
        finally
        {
            // Always release the per-host semaphore
            hostConnectionLock.Release();
        }
    }

    /// <summary>
    /// Creates a session for a host without raising the SessionCreated event.
    /// Used when the caller wants to manage pane creation manually.
    /// </summary>
    public async Task<TerminalSession?> CreateSessionForHostAsync(HostEntry host)
    {
        // Quick rejection if already connecting
        if (_connectingHosts.ContainsKey(host.Id))
        {
            _logger.LogWarning("Session creation for {DisplayName} already in progress, ignoring duplicate request", host.DisplayName);
            return null;
        }

        var hostConnectionLock = GetHostConnectionLock(host.Id);

        // Acquire per-host connection lock for thread safety
        if (!await hostConnectionLock.WaitAsync(TimeSpan.FromSeconds(Constants.ConnectionDefaults.ConnectionLockTimeoutSeconds)))
        {
            _logger.LogWarning("Failed to acquire host connection lock for session creation of {DisplayName} within {Timeout}s timeout", host.DisplayName, Constants.ConnectionDefaults.ConnectionLockTimeoutSeconds);
            return null;
        }

        try
        {
            // Double-check inside semaphore
            if (!_connectingHosts.TryAdd(host.Id, host.DisplayName))
            {
                _logger.LogWarning("Session creation for {DisplayName} already in progress (race condition), aborting", host.DisplayName);
                return null;
            }

            try
            {
                _logger.LogInformation("Creating session for {DisplayName} ({Hostname}:{Port}) using {AuthType}",
                    host.DisplayName, host.Hostname, host.Port, host.AuthType);

                var settings = await _settingsRepo.GetAsync();

                // Get password - check cache first, then fall back to stored password
                string? password = GetPasswordForHost(host, settings);

                // Create session
                var session = _sessionManager.CreateSession(host.DisplayName);
                session.Host = host;
                session.DecryptedPassword = password?.ToSecureString();

                // Notify that Sessions changed now that Host is set (for active session indicators)
                OnPropertyChanged(nameof(Sessions));

                _logger.LogDebug("Terminal session {SessionId} created for host {DisplayName}", session.Id, host.DisplayName);

                // Initialize session logging if enabled
                InitializeSessionLogging(session, host, settings);

                return session;
            }
            finally
            {
                RemoveConnectingHost(host.Id);
            }
        }
        finally
        {
            hostConnectionLock.Release();
        }
    }

    [RelayCommand]
    private async Task CloseSessionAsync(TerminalSession? session)
    {
        if (session != null)
        {
            await _sessionManager.CloseSessionAsync(session.Id);
        }
    }

    /// <summary>
    /// Creates connection info from a host entry and optional password.
    /// </summary>
    public async Task<TerminalConnectionInfo> CreateConnectionInfoAsync(HostEntry host, string? password)
    {
        var settings = await _settingsRepo.GetAsync();

        var timeoutSeconds = settings.ConnectionTimeoutSeconds > 0
            ? settings.ConnectionTimeoutSeconds
            : 30;
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);

        TimeSpan? keepAlive = null;
        if (settings.KeepAliveIntervalSeconds > 0)
        {
            keepAlive = TimeSpan.FromSeconds(settings.KeepAliveIntervalSeconds);
        }

        return TerminalConnectionInfo.FromHostEntry(host, password, timeout, keepAlive);
    }

    /// <summary>
    /// Records a connection result in the connection history.
    /// </summary>
    public async Task RecordConnectionResultAsync(
        HostEntry host,
        bool wasSuccessful,
        string? errorMessage,
        DateTimeOffset? connectedAt = null)
    {
        try
        {
            var trimmedError = errorMessage;
            if (!string.IsNullOrEmpty(trimmedError) && trimmedError.Length > 1000)
            {
                trimmedError = trimmedError[..1000];
            }

            await _historyRepo.AddAsync(new ConnectionHistory
            {
                HostId = host.Id,
                ConnectedAt = connectedAt ?? DateTimeOffset.UtcNow,
                WasSuccessful = wasSuccessful,
                ErrorMessage = wasSuccessful ? null : trimmedError
            });

            _logger.LogDebug("Connection history recorded for host {HostId} (success: {WasSuccessful})",
                host.Id, wasSuccessful);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record connection history for host {HostId}", host.Id);
        }
    }

    /// <summary>
    /// Creates a keyboard-interactive authentication callback that shows a dialog for 2FA/TOTP prompts.
    /// </summary>
    public KeyboardInteractiveCallback CreateKeyboardInteractiveCallback()
    {
        return async (request) =>
        {
            _logger.LogDebug("Received keyboard-interactive request: {Name} with {PromptCount} prompts",
                request.Name, request.Prompts.Count);

            // Check if application is available before showing dialog
            if (Application.Current?.Dispatcher == null)
            {
                _logger.LogWarning("Cannot show keyboard-interactive dialog - application is shutting down");
                return null;
            }

            // Show dialog on UI thread
            var result = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var dialog = new KeyboardInteractiveDialog();
                dialog.Owner = Application.Current.MainWindow;
                dialog.Initialize(request);
                dialog.ShowDialog();
                return dialog.GetResponseRequest();
            });

            if (result != null)
            {
                _logger.LogDebug("User provided responses to {PromptCount} prompts", result.Prompts.Count);
            }
            else
            {
                _logger.LogInformation("User cancelled keyboard-interactive authentication");
            }

            return result;
        };
    }

    /// <summary>
    /// Creates a host key verification callback for the specified host.
    /// Supports multiple key algorithms per host (RSA, ED25519, ECDSA, etc.).
    /// </summary>
    public HostKeyVerificationCallback CreateHostKeyVerificationCallback(Guid hostId)
    {
        return HostKeyVerificationHelper.CreateCallback(hostId, _fingerprintRepo, _logger);
    }

    /// <summary>
    /// Caches a credential for the specified host using the configured cache timeout.
    /// </summary>
    public void CacheCredentialForHost(Guid hostId, string value, CredentialType type)
    {
        try
        {
            if (_credentialCache is SecureCredentialCache secureCache)
            {
                var credential = secureCache.CreateCredential(type, value);
                _credentialCache.CacheCredential(hostId, credential);
                _logger.LogDebug("Cached {CredentialType} credential for host {HostId}", type, hostId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache credential for host {HostId}", hostId);
        }
    }

    private static SessionLogLevel ParseSessionLogLevel(string? value)
    {
        return Enum.TryParse(value, true, out SessionLogLevel parsed)
            ? parsed
            : SessionLogLevel.OutputAndEvents;
    }

    /// <summary>
    /// Retrieves the password for a host entry, checking the cache first and falling back to the stored encrypted password.
    /// If credential caching is enabled and a password is retrieved from storage, it will be cached for future use.
    /// </summary>
    /// <param name="host">The host entry to retrieve the password for.</param>
    /// <param name="settings">The application settings.</param>
    /// <returns>The decrypted password, or null if no password is available or decryption fails.</returns>
    private string? GetPasswordForHost(HostEntry host, AppSettings settings)
    {
        if (host.AuthType != AuthType.Password)
        {
            return null;
        }

        string? password = null;

        // Try to get cached credential first
        if (settings.EnableCredentialCaching)
        {
            var cachedCredential = _credentialCache.GetCachedCredential(host.Id);
            if (cachedCredential != null && cachedCredential.Type == CredentialType.Password)
            {
                password = cachedCredential.GetValue();
                _logger.LogDebug("Using cached password for host {DisplayName}", host.DisplayName);
                return password;
            }
        }

        // Fall back to stored password if no cached credential
        if (!string.IsNullOrEmpty(host.PasswordProtected))
        {
            password = _secretProtector.TryUnprotect(host.PasswordProtected);
            if (password == null)
            {
                _logger.LogWarning("Failed to decrypt password for host {DisplayName}", host.DisplayName);
            }
            else
            {
                _logger.LogDebug("Password decrypted successfully for host {DisplayName}", host.DisplayName);

                // Cache the credential for future connections if caching is enabled
                if (settings.EnableCredentialCaching)
                {
                    CacheCredentialForHost(host.Id, password, CredentialType.Password);
                }
            }
        }

        return password;
    }

    /// <summary>
    /// Initializes session logging for a terminal session based on application settings.
    /// </summary>
    /// <param name="session">The terminal session to initialize logging for.</param>
    /// <param name="host">The host entry associated with the session.</param>
    /// <param name="settings">The application settings.</param>
    private void InitializeSessionLogging(TerminalSession session, HostEntry host, AppSettings settings)
    {
        try
        {
            if (!settings.EnableSessionLogging)
            {
                return;
            }

            // Apply custom directory if set
            if (!string.IsNullOrEmpty(settings.SessionLogDirectory))
            {
                _sessionLoggingService.SetLogDirectory(settings.SessionLogDirectory);
            }
            _sessionLoggingService.SetTimestampEachLine(settings.SessionLogTimestampLines);
            _sessionLoggingService.SetMaxLogFileSizeMB(settings.MaxLogFileSizeMB);
            _sessionLoggingService.SetMaxLogFilesToKeep(settings.MaxLogFilesToKeep);

            var sessionTitle = $"{host.DisplayName}_{host.Hostname}";
            var logLevel = ParseSessionLogLevel(settings.SessionLogLevel);
            session.LogLevel = logLevel;
            session.RedactTypedSecrets = settings.RedactTypedSecrets;
            session.SessionLogger = _sessionLoggingService.StartLogging(
                session.Id,
                sessionTitle,
                logLevel,
                session.RedactTypedSecrets);
            session.SessionLogger.LogEvent("CONNECT", $"Connecting to {host.Hostname}:{host.Port} as {host.Username}");
            _logger.LogDebug("Session logging started for session {SessionId}", session.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize session logging for session {SessionId}", session.Id);
        }
    }

    public void Dispose()
    {
        _sessionManager.SessionCreated -= OnSessionCreated;
        _sessionManager.SessionClosed -= OnSessionClosed;
        _sessionManager.CurrentSessionChanged -= OnCurrentSessionChanged;

        foreach (var semaphore in _hostConnectionLocks.Values)
        {
            semaphore.Dispose();
        }

        _hostConnectionLocks.Clear();
        _connectingHosts.Clear();
    }
}
