using CommunityToolkit.Mvvm.ComponentModel;

namespace SshManager.Terminal.Models;

/// <summary>
/// Statistics for a terminal session including uptime, latency, and server resource usage.
/// </summary>
public sealed partial class TerminalStats : ObservableObject
{
    /// <summary>
    /// Connection uptime.
    /// </summary>
    [ObservableProperty]
    private TimeSpan _uptime;

    /// <summary>
    /// Current latency to the server (from ping).
    /// </summary>
    [ObservableProperty]
    private TimeSpan? _latency;

    /// <summary>
    /// Server CPU usage percentage (0-100).
    /// </summary>
    [ObservableProperty]
    private double? _cpuUsage;

    /// <summary>
    /// Server memory usage percentage (0-100).
    /// </summary>
    [ObservableProperty]
    private double? _memoryUsage;

    /// <summary>
    /// Server disk usage percentage for root filesystem (0-100).
    /// </summary>
    [ObservableProperty]
    private double? _diskUsage;

    /// <summary>
    /// Server uptime (how long the server has been running).
    /// </summary>
    [ObservableProperty]
    private TimeSpan? _serverUptime;

    /// <summary>
    /// Total bytes sent during this session.
    /// </summary>
    [ObservableProperty]
    private long _bytesSent;

    /// <summary>
    /// Total bytes received during this session.
    /// </summary>
    [ObservableProperty]
    private long _bytesReceived;

    /// <summary>
    /// Current upload throughput in bytes per second.
    /// </summary>
    [ObservableProperty]
    private double _bytesSentPerSecond;

    /// <summary>
    /// Current download throughput in bytes per second.
    /// </summary>
    [ObservableProperty]
    private double _bytesReceivedPerSecond;

    /// <summary>
    /// Whether stats collection is enabled/available.
    /// </summary>
    [ObservableProperty]
    private bool _isCollecting;

    /// <summary>
    /// When the session started (for uptime calculation).
    /// </summary>
    public DateTimeOffset SessionStartTime { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Formats bytes as a human-readable string.
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    /// <summary>
    /// Formats bytes per second as a human-readable throughput string.
    /// </summary>
    public static string FormatThroughput(double bytesPerSecond)
    {
        if (bytesPerSecond < 1024) return $"{bytesPerSecond:F0} B/s";
        if (bytesPerSecond < 1024 * 1024) return $"{bytesPerSecond / 1024.0:F1} KB/s";
        return $"{bytesPerSecond / (1024.0 * 1024):F1} MB/s";
    }
}
