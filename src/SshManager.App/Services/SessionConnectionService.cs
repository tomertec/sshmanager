using Microsoft.Extensions.Logging;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Security;
using SshManager.Terminal;
using SshManager.Terminal.Models;
using SshManager.Terminal.Services;

namespace SshManager.App.Services;

/// <summary>
/// Service for establishing and managing session connections (SSH and Serial).
/// Handles connection orchestration, proxy chain resolution, session logging,
/// connection history recording, and port forwarding setup.
/// </summary>
public sealed class SessionConnectionService : ISessionConnectionService
{
    private readonly ISshConnectionService _sshService;
    private readonly ISerialConnectionService _serialService;
    private readonly IProxyJumpService _proxyJumpService;
    private readonly IPortForwardingService _portForwardingService;
    private readonly IConnectionHistoryRepository _historyRepository;
    private readonly ISettingsRepository _settingsRepository;
    private readonly ILogger<SessionConnectionService> _logger;

    /// <summary>
    /// Event raised when a connection attempt completes (success or failure).
    /// </summary>
    public event EventHandler<SessionConnectionResultEventArgs>? ConnectionCompleted;

    public SessionConnectionService(
        ISshConnectionService sshService,
        ISerialConnectionService serialService,
        IProxyJumpService proxyJumpService,
        IPortForwardingService portForwardingService,
        IConnectionHistoryRepository historyRepository,
        ISettingsRepository settingsRepository,
        ILogger<SessionConnectionService> logger)
    {
        _sshService = sshService ?? throw new ArgumentNullException(nameof(sshService));
        _serialService = serialService ?? throw new ArgumentNullException(nameof(serialService));
        _proxyJumpService = proxyJumpService ?? throw new ArgumentNullException(nameof(proxyJumpService));
        _portForwardingService = portForwardingService ?? throw new ArgumentNullException(nameof(portForwardingService));
        _historyRepository = historyRepository ?? throw new ArgumentNullException(nameof(historyRepository));
        _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Connects an SSH session to a terminal pane target.
    /// Handles both direct connections and proxy jump chains.
    /// </summary>
    public async Task ConnectSshSessionAsync(
        ITerminalPaneTarget paneTarget,
        TerminalSession session,
        CancellationToken cancellationToken = default)
    {
        if (paneTarget == null)
            throw new ArgumentNullException(nameof(paneTarget));
        if (session == null)
            throw new ArgumentNullException(nameof(session));
        if (session.Host == null)
            throw new InvalidOperationException("Session must have a host entry");

        var host = session.Host;
        var connectionStartedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "Connecting SSH session {SessionId} to {DisplayName} ({Hostname}:{Port})",
            session.Id, host.DisplayName, host.Hostname, host.Port);

        try
        {
            // Create host key verification callback using the SessionViewModel pattern
            var hostKeyCallback = CreateHostKeyVerificationCallback(host.Id);
            var kbInteractiveCallback = CreateKeyboardInteractiveCallback();

            // Check if host has a ProxyJump profile configured
            if (host.ProxyJumpProfileId.HasValue)
            {
                await ConnectViaProxyJumpAsync(
                    paneTarget,
                    session,
                    host,
                    hostKeyCallback,
                    kbInteractiveCallback,
                    cancellationToken);
            }
            else
            {
                // Direct connection (no proxy jump)
                await ConnectDirectAsync(
                    paneTarget,
                    session,
                    host,
                    hostKeyCallback,
                    kbInteractiveCallback,
                    cancellationToken);
            }

            // Update session status
            session.Status = "Connected";

            // Record successful connection
            await RecordConnectionResultAsync(host, true, null, connectionStartedAt);

            _logger.LogInformation(
                "SSH session {SessionId} connected successfully to {DisplayName}",
                session.Id, host.DisplayName);

            // Focus the terminal after connection
            _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                paneTarget.TerminalControl.FocusInput());

            // Start auto-start port forwardings after successful connection
            await StartAutoStartPortForwardingsAsync(session, cancellationToken);

            // Subscribe to session disconnection to clean up port forwardings
            session.SessionClosed += OnSessionClosedForPortForwarding;

            // Raise success event
            ConnectionCompleted?.Invoke(
                this,
                SessionConnectionResultEventArgs.CreateSuccess(session));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to connect SSH session {SessionId} to {DisplayName}: {ErrorMessage}",
                session.Id, host.DisplayName, ex.Message);

            session.Status = $"Failed: {ex.Message}";
            session.SessionLogger?.LogEvent("ERROR", $"Connection failed: {ex.Message}");

            // Record failed connection
            await RecordConnectionResultAsync(host, false, ex.Message, connectionStartedAt);

            // Raise failure event
            ConnectionCompleted?.Invoke(
                this,
                SessionConnectionResultEventArgs.CreateFailure(session, ex));

            throw;
        }
    }

    /// <summary>
    /// Connects a serial port session to a terminal pane target.
    /// </summary>
    public async Task ConnectSerialSessionAsync(
        ITerminalPaneTarget paneTarget,
        TerminalSession session,
        CancellationToken cancellationToken = default)
    {
        if (paneTarget == null)
            throw new ArgumentNullException(nameof(paneTarget));
        if (session == null)
            throw new ArgumentNullException(nameof(session));
        if (session.Host == null)
            throw new InvalidOperationException("Session must have a host entry");

        var host = session.Host;
        var connectionStartedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "Connecting serial session {SessionId} to {PortName} at {BaudRate} baud",
            session.Id, host.SerialPortName, host.SerialBaudRate);

        try
        {
            var connectionInfo = SerialConnectionInfo.FromHostEntry(host);

            // Connect to the serial port
            await paneTarget.ConnectSerialAsync(
                _serialService,
                connectionInfo,
                session,
                cancellationToken);

            session.Status = "Connected";
            session.SessionLogger?.LogEvent(
                "CONNECT",
                $"Connected to serial port {host.SerialPortName} at {connectionInfo.GetDisplayString()}");

            await RecordConnectionResultAsync(host, true, null, connectionStartedAt);

            _logger.LogInformation(
                "Serial session {SessionId} connected successfully to {PortName}",
                session.Id, host.SerialPortName);

            // Focus the terminal after connection
            _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                paneTarget.TerminalControl.FocusInput());

            // Raise success event
            ConnectionCompleted?.Invoke(
                this,
                SessionConnectionResultEventArgs.CreateSuccess(session));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to connect serial session {SessionId} to {PortName}: {ErrorMessage}",
                session.Id, host.SerialPortName, ex.Message);

            session.Status = $"Failed: {ex.Message}";
            session.SessionLogger?.LogEvent("ERROR", $"Serial connection failed: {ex.Message}");

            await RecordConnectionResultAsync(host, false, ex.Message, connectionStartedAt);

            // Raise failure event
            ConnectionCompleted?.Invoke(
                this,
                SessionConnectionResultEventArgs.CreateFailure(session, ex));

            throw;
        }
    }

    /// <summary>
    /// Starts auto-start port forwardings for a successfully connected SSH session.
    /// </summary>
    public async Task StartAutoStartPortForwardingsAsync(
        TerminalSession session,
        CancellationToken cancellationToken = default)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));

        if (session.Host == null || session.Connection == null)
        {
            _logger.LogDebug(
                "Skipping auto-start port forwardings for session {SessionId} - no host or connection",
                session.Id);
            return;
        }

        try
        {
            _logger.LogDebug(
                "Starting auto-start port forwardings for session {SessionId}",
                session.Id);

            // Start auto-start port forwardings via the service
            var handles = await _portForwardingService.StartAutoStartForwardingsAsync(
                session.Connection,
                session.Id,
                session.Host.Id,
                cancellationToken);

            if (handles.Count > 0)
            {
                session.SessionLogger?.LogEvent(
                    "PORT_FORWARD",
                    $"Started {handles.Count} auto-start port forwarding(s)");

                _logger.LogInformation(
                    "Started {Count} auto-start port forwardings for session {SessionId}",
                    handles.Count, session.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to start auto-start port forwardings for session {SessionId}: {ErrorMessage}",
                session.Id, ex.Message);

            session.SessionLogger?.LogEvent(
                "ERROR",
                $"Failed to start auto-start port forwardings: {ex.Message}");

            // Don't throw - port forwarding failures shouldn't fail the connection
        }
    }

    /// <summary>
    /// Connects directly to an SSH host without proxy jump.
    /// </summary>
    private async Task ConnectDirectAsync(
        ITerminalPaneTarget paneTarget,
        TerminalSession session,
        HostEntry host,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Establishing direct SSH connection to {Hostname}:{Port}",
            host.Hostname, host.Port);

        var connectionInfo = await CreateConnectionInfoAsync(
            host,
            session.DecryptedPassword?.ToUnsecureString());

        await paneTarget.ConnectAsync(
            _sshService,
            connectionInfo,
            hostKeyCallback,
            kbInteractiveCallback);

        _logger.LogDebug(
            "Direct SSH connection established to {Hostname}:{Port}",
            host.Hostname, host.Port);
    }

    /// <summary>
    /// Connects to an SSH host via a proxy jump chain.
    /// </summary>
    private async Task ConnectViaProxyJumpAsync(
        ITerminalPaneTarget paneTarget,
        TerminalSession session,
        HostEntry host,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Resolving proxy jump chain for {Hostname}:{Port}",
            host.Hostname, host.Port);

        // Build passwords dictionary for the chain
        var passwords = new Dictionary<Guid, string>();
        if (session.DecryptedPassword != null)
        {
            passwords[host.Id] = session.DecryptedPassword.ToUnsecureString()!;
        }

        // Resolve the connection chain
        var connectionChain = await _proxyJumpService.ResolveConnectionChainAsync(
            host,
            passwords,
            cancellationToken);

        if (connectionChain.Count > 0)
        {
            // Connect through the proxy chain
            var chainDescription = string.Join(" → ", connectionChain.Select(c => c.Hostname));
            session.SessionLogger?.LogEvent(
                "CONNECT",
                $"Connecting via proxy chain: {chainDescription}");

            _logger.LogInformation(
                "Connecting to {Hostname}:{Port} via proxy chain: {Chain}",
                host.Hostname, host.Port, chainDescription);

            await paneTarget.ConnectWithProxyChainAsync(
                _sshService,
                connectionChain,
                hostKeyCallback,
                kbInteractiveCallback);

            _logger.LogDebug(
                "Proxy jump connection established to {Hostname}:{Port}",
                host.Hostname, host.Port);
        }
        else
        {
            // Fallback to direct connection if chain resolution returned empty
            _logger.LogWarning(
                "Proxy jump chain resolution returned empty for {Hostname}:{Port}, falling back to direct connection",
                host.Hostname, host.Port);

            await ConnectDirectAsync(
                paneTarget,
                session,
                host,
                hostKeyCallback,
                kbInteractiveCallback,
                cancellationToken);
        }
    }

    /// <summary>
    /// Creates connection info from a host entry and optional password.
    /// </summary>
    private async Task<TerminalConnectionInfo> CreateConnectionInfoAsync(
        HostEntry host,
        string? password)
    {
        var settings = await _settingsRepository.GetAsync();

        var timeoutSeconds = settings.ConnectionTimeoutSeconds > 0
            ? settings.ConnectionTimeoutSeconds
            : 30;
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);

        // Determine keep-alive interval using precedence:
        // 1. Per-host setting (if specified)
        // 2. Global setting (fallback)
        TimeSpan? keepAlive = null;
        if (host.KeepAliveIntervalSeconds.HasValue)
        {
            // Use per-host setting (0 = disable, >0 = interval)
            if (host.KeepAliveIntervalSeconds.Value > 0)
            {
                keepAlive = TimeSpan.FromSeconds(host.KeepAliveIntervalSeconds.Value);
                _logger.LogDebug(
                    "Using per-host keep-alive interval: {Interval} seconds for host {HostId}",
                    host.KeepAliveIntervalSeconds.Value, host.Id);
            }
            else
            {
                _logger.LogDebug(
                    "Keep-alive disabled for host {HostId} via per-host setting",
                    host.Id);
            }
        }
        else
        {
            // Use global setting
            if (settings.KeepAliveIntervalSeconds > 0)
            {
                keepAlive = TimeSpan.FromSeconds(settings.KeepAliveIntervalSeconds);
                _logger.LogDebug(
                    "Using global keep-alive interval: {Interval} seconds for host {HostId}",
                    settings.KeepAliveIntervalSeconds, host.Id);
            }
        }

        return TerminalConnectionInfo.FromHostEntry(host, password, timeout, keepAlive);
    }

    /// <summary>
    /// Records a connection result in the connection history.
    /// </summary>
    private async Task RecordConnectionResultAsync(
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

            await _historyRepository.AddAsync(new ConnectionHistory
            {
                HostId = host.Id,
                ConnectedAt = connectedAt ?? DateTimeOffset.UtcNow,
                WasSuccessful = wasSuccessful,
                ErrorMessage = wasSuccessful ? null : trimmedError
            });

            _logger.LogDebug(
                "Connection history recorded for host {HostId} (success: {WasSuccessful})",
                host.Id, wasSuccessful);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to record connection history for host {HostId}",
                host.Id);
        }
    }

    /// <summary>
    /// Creates a keyboard-interactive authentication callback that shows a dialog for 2FA/TOTP prompts.
    /// This follows the pattern from SessionViewModel.CreateKeyboardInteractiveCallback.
    /// </summary>
    private KeyboardInteractiveCallback CreateKeyboardInteractiveCallback()
    {
        return async (request) =>
        {
            _logger.LogDebug(
                "Received keyboard-interactive request: {Name} with {PromptCount} prompts",
                request.Name, request.Prompts.Count);

            // Check if application is available before showing dialog
            if (System.Windows.Application.Current?.Dispatcher == null)
            {
                _logger.LogWarning("Cannot show keyboard-interactive dialog - application is shutting down");
                return null;
            }

            // Show dialog on UI thread
            var result = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var dialog = new Views.Dialogs.KeyboardInteractiveDialog();
                dialog.Owner = System.Windows.Application.Current.MainWindow;
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
    private HostKeyVerificationCallback CreateHostKeyVerificationCallback(Guid hostId)
    {
        return async (hostname, port, algorithm, fingerprint, keyBytes) =>
        {
            _logger.LogDebug("Verifying host key for {Hostname}:{Port} - {Algorithm}", hostname, port, algorithm);

            // Get existing fingerprint from database
            var fingerprintRepo = App.GetService<IHostFingerprintRepository>();
            if (fingerprintRepo == null)
            {
                _logger.LogWarning("Host fingerprint repository not available, cannot verify host key");
                return false;
            }

            // Look up fingerprint by host AND algorithm (supports multiple key types per host)
            var existingFingerprint = await fingerprintRepo.GetByHostAndAlgorithmAsync(hostId, algorithm);

            // Check if fingerprint matches for this specific algorithm
            if (existingFingerprint != null && existingFingerprint.Fingerprint == fingerprint)
            {
                // Fingerprint matches - update last seen and trust
                await fingerprintRepo.UpdateLastSeenAsync(existingFingerprint.Id);
                _logger.LogDebug("Host key verified - fingerprint matches stored value for {Algorithm}", algorithm);
                return true;
            }

            // Check if application is available before showing dialog
            if (System.Windows.Application.Current?.Dispatcher == null)
            {
                _logger.LogWarning("Cannot show host key verification dialog - application is shutting down");
                return false;
            }

            // Show verification dialog on UI thread
            // Pass existingFingerprint to show if key changed for this algorithm
            var accepted = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var dialog = new Views.Dialogs.HostKeyVerificationDialog();
                dialog.Owner = System.Windows.Application.Current.MainWindow;
                dialog.Initialize(hostname, port, algorithm, fingerprint, existingFingerprint);
                dialog.ShowDialog();
                return dialog.IsAccepted;
            });

            if (accepted)
            {
                if (existingFingerprint != null)
                {
                    // Update existing fingerprint for this algorithm (key changed)
                    existingFingerprint.Fingerprint = fingerprint;
                    existingFingerprint.LastSeen = DateTimeOffset.UtcNow;
                    existingFingerprint.IsTrusted = true;
                    await fingerprintRepo.UpdateAsync(existingFingerprint);
                    _logger.LogInformation("Updated host key fingerprint for {Hostname}:{Port} ({Algorithm})", hostname, port, algorithm);
                }
                else
                {
                    // Add new fingerprint for this algorithm
                    // This allows storing multiple algorithms per host (RSA, ED25519, ECDSA, etc.)
                    var newFingerprint = new HostFingerprint
                    {
                        HostId = hostId,
                        Algorithm = algorithm,
                        Fingerprint = fingerprint,
                        FirstSeen = DateTimeOffset.UtcNow,
                        LastSeen = DateTimeOffset.UtcNow,
                        IsTrusted = true
                    };
                    await fingerprintRepo.AddAsync(newFingerprint);
                    _logger.LogInformation("Stored new host key fingerprint for {Hostname}:{Port} ({Algorithm})", hostname, port, algorithm);
                }
            }
            else
            {
                _logger.LogWarning("Host key rejected by user for {Hostname}:{Port} ({Algorithm})", hostname, port, algorithm);
            }

            return accepted;
        };
    }

    /// <summary>
    /// Handles session close event to clean up port forwardings.
    /// </summary>
    private async void OnSessionClosedForPortForwarding(object? sender, EventArgs e)
    {
        if (sender is not TerminalSession session)
            return;

        // Unsubscribe to prevent memory leaks
        session.SessionClosed -= OnSessionClosedForPortForwarding;

        try
        {
            _logger.LogDebug(
                "Stopping port forwardings for closed session {SessionId}",
                session.Id);

            // Stop all port forwardings for this session
            await _portForwardingService.StopAllForSessionAsync(session.Id);

            _logger.LogDebug(
                "Port forwardings stopped for session {SessionId}",
                session.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to stop port forwardings for session {SessionId}",
                session.Id);
        }
    }
}
