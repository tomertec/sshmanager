using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Renci.SshNet;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service implementation for managing SSH port forwarding.
/// </summary>
public sealed class PortForwardingService : IPortForwardingService
{
    private readonly IPortForwardingProfileRepository _profileRepository;
    private readonly ILogger<PortForwardingService> _logger;
    private readonly ConcurrentDictionary<Guid, ActivePortForwarding> _activeForwardings = new();
    private readonly object _lock = new();

    /// <inheritdoc />
    public event EventHandler<PortForwardingStatusChangedEventArgs>? StatusChanged;

    public PortForwardingService(
        IPortForwardingProfileRepository profileRepository,
        ILogger<PortForwardingService>? logger = null)
    {
        _profileRepository = profileRepository;
        _logger = logger ?? NullLogger<PortForwardingService>.Instance;
    }

    /// <inheritdoc />
    public async Task<PortForwardingHandle?> StartForwardingAsync(
        ISshConnection connection,
        Guid sessionId,
        PortForwardingProfile profile,
        CancellationToken ct = default)
    {
        if (!profile.IsEnabled)
        {
            _logger.LogWarning("Port forwarding profile {ProfileName} is disabled", profile.DisplayName);
            return null;
        }

        // Check if local port is already in use
        if (profile.ForwardingType != PortForwardingType.RemoteForward)
        {
            if (IsLocalPortInUse(profile.LocalPort))
            {
                _logger.LogError("Local port {Port} is already in use by another forwarding", profile.LocalPort);
                return null;
            }

            // Also check if the OS has the port in use
            if (IsSystemPortInUse(profile.LocalBindAddress, profile.LocalPort))
            {
                _logger.LogError("Local port {Port} is already in use by the system", profile.LocalPort);
                return null;
            }
        }

        var forwardingId = Guid.NewGuid();
        var activeForwarding = new ActivePortForwarding
        {
            Id = forwardingId,
            SessionId = sessionId,
            Profile = profile,
            Status = PortForwardingStatus.Starting,
            StartedAt = DateTimeOffset.UtcNow
        };

        _activeForwardings[forwardingId] = activeForwarding;

        try
        {
            // Get the underlying SshClient from the connection
            var sshClient = GetSshClientFromConnection(connection);
            if (sshClient is null)
            {
                throw new InvalidOperationException(
                    "Cannot start port forwarding: Unable to access SSH client from connection.");
            }

            ForwardedPort forwardedPort = profile.ForwardingType switch
            {
                PortForwardingType.LocalForward => CreateLocalForward(profile),
                PortForwardingType.RemoteForward => CreateRemoteForward(profile),
                PortForwardingType.DynamicForward => CreateDynamicForward(profile),
                _ => throw new ArgumentOutOfRangeException(nameof(profile.ForwardingType))
            };

            // Add event handlers
            forwardedPort.Exception += (sender, e) =>
            {
                _logger.LogError(e.Exception, "Port forwarding {ProfileName} error", profile.DisplayName);
                UpdateForwardingStatus(forwardingId, PortForwardingStatus.Failed, e.Exception.Message);
            };

            forwardedPort.RequestReceived += (sender, e) =>
            {
                _logger.LogDebug("Port forwarding {ProfileName} received request from {Origin}",
                    profile.DisplayName, e.OriginatorHost);
            };

            // Add to client and start
            sshClient.AddForwardedPort(forwardedPort);
            await Task.Run(() => forwardedPort.Start(), ct);

            var handle = new PortForwardingHandle
            {
                Id = forwardingId,
                SessionId = sessionId,
                ProfileId = profile.Id,
                StartedAt = DateTimeOffset.UtcNow,
                ForwardedPort = forwardedPort,
                StopAction = () =>
                {
                    try
                    {
                        forwardedPort.Stop();
                        sshClient.RemoveForwardedPort(forwardedPort);
                        forwardedPort.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error stopping port forwarding");
                    }
                }
            };

            activeForwarding.Handle = handle;
            UpdateForwardingStatus(forwardingId, PortForwardingStatus.Active);

            _logger.LogInformation(
                "Started {ForwardType} port forwarding: {Description}",
                profile.ForwardingType,
                activeForwarding.GetDisplayDescription());

            return handle;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start port forwarding {ProfileName}", profile.DisplayName);
            UpdateForwardingStatus(forwardingId, PortForwardingStatus.Failed, ex.Message);
            _activeForwardings.TryRemove(forwardingId, out _);
            return null;
        }
    }

    /// <inheritdoc />
    public Task StopForwardingAsync(PortForwardingHandle handle, CancellationToken ct = default)
    {
        return StopForwardingAsync(handle.Id, ct);
    }

    /// <inheritdoc />
    public Task StopForwardingAsync(Guid forwardingId, CancellationToken ct = default)
    {
        if (!_activeForwardings.TryRemove(forwardingId, out var forwarding))
        {
            _logger.LogDebug("Forwarding {ForwardingId} not found or already stopped", forwardingId);
            return Task.CompletedTask;
        }

        return Task.Run(() =>
        {
            try
            {
                forwarding.Handle?.StopAction?.Invoke();
                UpdateForwardingStatus(forwarding, PortForwardingStatus.Stopped);
                _logger.LogInformation("Stopped port forwarding: {Description}",
                    forwarding.GetDisplayDescription());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping port forwarding {ForwardingId}", forwardingId);
            }
        }, ct);
    }

    /// <inheritdoc />
    public IReadOnlyList<ActivePortForwarding> GetActiveForwardings()
    {
        return _activeForwardings.Values
            .Where(f => f.Status == PortForwardingStatus.Active || f.Status == PortForwardingStatus.Starting)
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<ActivePortForwarding> GetActiveForwardings(Guid sessionId)
    {
        return _activeForwardings.Values
            .Where(f => f.SessionId == sessionId &&
                       (f.Status == PortForwardingStatus.Active || f.Status == PortForwardingStatus.Starting))
            .ToList();
    }

    /// <inheritdoc />
    public async Task StopAllForSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var forwardings = _activeForwardings.Values
            .Where(f => f.SessionId == sessionId)
            .ToList();

        _logger.LogInformation("Stopping {Count} port forwardings for session {SessionId}",
            forwardings.Count, sessionId);

        foreach (var forwarding in forwardings)
        {
            await StopForwardingAsync(forwarding.Id, ct);
        }
    }

    /// <inheritdoc />
    public async Task StopAllAsync(CancellationToken ct = default)
    {
        var forwardings = _activeForwardings.Values.ToList();

        _logger.LogInformation("Stopping all {Count} port forwardings", forwardings.Count);

        foreach (var forwarding in forwardings)
        {
            await StopForwardingAsync(forwarding.Id, ct);
        }
    }

    /// <inheritdoc />
    public bool IsLocalPortInUse(int localPort)
    {
        return _activeForwardings.Values.Any(f =>
            f.Profile.LocalPort == localPort &&
            f.Profile.ForwardingType != PortForwardingType.RemoteForward &&
            (f.Status == PortForwardingStatus.Active || f.Status == PortForwardingStatus.Starting));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PortForwardingHandle>> StartAutoStartForwardingsAsync(
        ISshConnection connection,
        Guid sessionId,
        Guid hostId,
        CancellationToken ct = default)
    {
        var profiles = await _profileRepository.GetByHostIdAsync(hostId, ct);
        var autoStartProfiles = profiles.Where(p => p.AutoStart && p.IsEnabled).ToList();

        if (autoStartProfiles.Count == 0)
        {
            _logger.LogDebug("No auto-start port forwarding profiles for host {HostId}", hostId);
            return [];
        }

        _logger.LogInformation("Starting {Count} auto-start port forwardings for host {HostId}",
            autoStartProfiles.Count, hostId);

        var handles = new List<PortForwardingHandle>();

        foreach (var profile in autoStartProfiles)
        {
            var handle = await StartForwardingAsync(connection, sessionId, profile, ct);
            if (handle is not null)
            {
                handles.Add(handle);
            }
        }

        return handles;
    }

    /// <summary>
    /// Attempts to get the underlying SshClient from an ISshConnection.
    /// </summary>
    private SshClient? GetSshClientFromConnection(ISshConnection connection)
    {
        // The SshConnection class is internal, so we use reflection to access the client
        var connectionType = connection.GetType();
        var clientField = connectionType.GetField("_client",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        return clientField?.GetValue(connection) as SshClient;
    }

    /// <summary>
    /// Creates a local port forwarding configuration.
    /// </summary>
    private static ForwardedPortLocal CreateLocalForward(PortForwardingProfile profile)
    {
        if (string.IsNullOrEmpty(profile.RemoteHost) || !profile.RemotePort.HasValue)
        {
            throw new InvalidOperationException(
                "Local forward requires RemoteHost and RemotePort to be specified.");
        }

        return new ForwardedPortLocal(
            profile.LocalBindAddress,
            (uint)profile.LocalPort,
            profile.RemoteHost,
            (uint)profile.RemotePort.Value);
    }

    /// <summary>
    /// Creates a remote port forwarding configuration.
    /// </summary>
    private static ForwardedPortRemote CreateRemoteForward(PortForwardingProfile profile)
    {
        if (string.IsNullOrEmpty(profile.RemoteHost) || !profile.RemotePort.HasValue)
        {
            throw new InvalidOperationException(
                "Remote forward requires RemoteHost and RemotePort to be specified.");
        }

        return new ForwardedPortRemote(
            (uint)profile.RemotePort.Value,
            profile.LocalBindAddress,
            (uint)profile.LocalPort);
    }

    /// <summary>
    /// Creates a dynamic (SOCKS5) port forwarding configuration.
    /// </summary>
    private static ForwardedPortDynamic CreateDynamicForward(PortForwardingProfile profile)
    {
        return new ForwardedPortDynamic(
            profile.LocalBindAddress,
            (uint)profile.LocalPort);
    }

    /// <summary>
    /// Checks if a port is in use by the operating system.
    /// </summary>
    private static bool IsSystemPortInUse(string bindAddress, int port)
    {
        try
        {
            var address = bindAddress == "0.0.0.0" || bindAddress == "*"
                ? IPAddress.Any
                : IPAddress.Parse(bindAddress);

            using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(address, port));
            return false;
        }
        catch (SocketException)
        {
            return true;
        }
    }

    /// <summary>
    /// Updates the status of a forwarding and raises the StatusChanged event.
    /// </summary>
    private void UpdateForwardingStatus(Guid forwardingId, PortForwardingStatus newStatus, string? errorMessage = null)
    {
        if (_activeForwardings.TryGetValue(forwardingId, out var forwarding))
        {
            UpdateForwardingStatus(forwarding, newStatus, errorMessage);
        }
    }

    /// <summary>
    /// Updates the status of a forwarding and raises the StatusChanged event.
    /// </summary>
    private void UpdateForwardingStatus(ActivePortForwarding forwarding, PortForwardingStatus newStatus, string? errorMessage = null)
    {
        var previousStatus = forwarding.Status;
        forwarding.Status = newStatus;
        forwarding.ErrorMessage = errorMessage;

        StatusChanged?.Invoke(this, new PortForwardingStatusChangedEventArgs
        {
            Forwarding = forwarding,
            PreviousStatus = previousStatus,
            NewStatus = newStatus
        });
    }
}
