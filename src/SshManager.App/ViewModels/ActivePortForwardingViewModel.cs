using CommunityToolkit.Mvvm.ComponentModel;
using SshManager.Core.Models;

namespace SshManager.App.ViewModels;

/// <summary>
/// Status of an active port forwarding.
/// </summary>
public enum PortForwardingStatus
{
    /// <summary>Port forwarding is starting.</summary>
    Starting,

    /// <summary>Port forwarding is active and working.</summary>
    Active,

    /// <summary>Port forwarding has failed.</summary>
    Failed,

    /// <summary>Port forwarding has been stopped.</summary>
    Stopped
}

/// <summary>
/// ViewModel representing an active port forwarding in a session.
/// </summary>
public partial class ActivePortForwardingViewModel : ObservableObject
{
    /// <summary>
    /// Unique identifier for this active forwarding.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// The session this forwarding belongs to.
    /// </summary>
    public Guid SessionId { get; init; }

    /// <summary>
    /// The session display name for UI binding.
    /// </summary>
    [ObservableProperty]
    private string _sessionName = string.Empty;

    /// <summary>
    /// The profile being used for this forwarding.
    /// </summary>
    public PortForwardingProfile Profile { get; init; } = null!;

    /// <summary>
    /// Current status of the port forwarding.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusDisplay))]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    private PortForwardingStatus _status = PortForwardingStatus.Starting;

    /// <summary>
    /// Error message if status is Failed.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// When the forwarding was started.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Number of bytes transferred through this forwarding.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BytesTransferredDisplay))]
    private long _bytesTransferred;

    /// <summary>
    /// Display name from the profile.
    /// </summary>
    public string DisplayName => Profile.DisplayName;

    /// <summary>
    /// Type of port forwarding.
    /// </summary>
    public PortForwardingType ForwardingType => Profile.ForwardingType;

    /// <summary>
    /// Local endpoint description.
    /// </summary>
    public string LocalEndpoint => $"{Profile.LocalBindAddress}:{Profile.LocalPort}";

    /// <summary>
    /// Remote endpoint description (empty for dynamic forwarding).
    /// </summary>
    public string RemoteEndpoint => ForwardingType == PortForwardingType.DynamicForward
        ? "(SOCKS5)"
        : $"{Profile.RemoteHost}:{Profile.RemotePort}";

    /// <summary>
    /// Full forwarding description for UI.
    /// </summary>
    public string ForwardingDescription => ForwardingType switch
    {
        PortForwardingType.LocalForward => $"{LocalEndpoint} -> {RemoteEndpoint}",
        PortForwardingType.RemoteForward => $"{RemoteEndpoint} <- {LocalEndpoint}",
        PortForwardingType.DynamicForward => $"{LocalEndpoint} (SOCKS5)",
        _ => LocalEndpoint
    };

    /// <summary>
    /// Human-readable status display.
    /// </summary>
    public string StatusDisplay => Status switch
    {
        PortForwardingStatus.Starting => "Starting...",
        PortForwardingStatus.Active => "Active",
        PortForwardingStatus.Failed => "Failed",
        PortForwardingStatus.Stopped => "Stopped",
        _ => "Unknown"
    };

    /// <summary>
    /// Status icon/indicator for UI.
    /// </summary>
    public string StatusIcon => Status switch
    {
        PortForwardingStatus.Starting => "HourglassHalf24",
        PortForwardingStatus.Active => "Circle24",
        PortForwardingStatus.Failed => "DismissCircle24",
        PortForwardingStatus.Stopped => "CircleOff24",
        _ => "Question24"
    };

    /// <summary>
    /// Whether the forwarding is currently active.
    /// </summary>
    public bool IsActive => Status == PortForwardingStatus.Active;

    /// <summary>
    /// Whether the forwarding can be stopped.
    /// </summary>
    public bool CanStop => Status is PortForwardingStatus.Starting or PortForwardingStatus.Active;

    /// <summary>
    /// Human-readable bytes transferred display.
    /// </summary>
    public string BytesTransferredDisplay
    {
        get
        {
            if (BytesTransferred < 1024)
                return $"{BytesTransferred} B";
            if (BytesTransferred < 1024 * 1024)
                return $"{BytesTransferred / 1024.0:F1} KB";
            if (BytesTransferred < 1024 * 1024 * 1024)
                return $"{BytesTransferred / (1024.0 * 1024):F1} MB";
            return $"{BytesTransferred / (1024.0 * 1024 * 1024):F2} GB";
        }
    }

    /// <summary>
    /// Duration since the forwarding started.
    /// </summary>
    public TimeSpan Duration => DateTimeOffset.UtcNow - StartedAt;

    /// <summary>
    /// Human-readable duration display.
    /// </summary>
    public string DurationDisplay
    {
        get
        {
            var duration = Duration;
            if (duration.TotalSeconds < 60)
                return $"{(int)duration.TotalSeconds}s";
            if (duration.TotalMinutes < 60)
                return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
            if (duration.TotalHours < 24)
                return $"{(int)duration.TotalHours}h {duration.Minutes}m";
            return $"{(int)duration.TotalDays}d {duration.Hours}h";
        }
    }

    /// <summary>
    /// Creates an ActivePortForwardingViewModel from a profile.
    /// </summary>
    public static ActivePortForwardingViewModel FromProfile(
        PortForwardingProfile profile,
        Guid sessionId,
        string sessionName)
    {
        return new ActivePortForwardingViewModel
        {
            SessionId = sessionId,
            SessionName = sessionName,
            Profile = profile,
            Status = PortForwardingStatus.Starting
        };
    }

    /// <summary>
    /// Updates the bytes transferred count.
    /// </summary>
    public void UpdateBytesTransferred(long bytes)
    {
        BytesTransferred = bytes;
    }

    /// <summary>
    /// Marks the forwarding as active.
    /// </summary>
    public void MarkActive()
    {
        Status = PortForwardingStatus.Active;
        ErrorMessage = null;
    }

    /// <summary>
    /// Marks the forwarding as failed.
    /// </summary>
    public void MarkFailed(string error)
    {
        Status = PortForwardingStatus.Failed;
        ErrorMessage = error;
    }

    /// <summary>
    /// Marks the forwarding as stopped.
    /// </summary>
    public void MarkStopped()
    {
        Status = PortForwardingStatus.Stopped;
    }
}
