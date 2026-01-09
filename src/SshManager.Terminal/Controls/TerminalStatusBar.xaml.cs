using System.Windows;
using System.Windows.Controls;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Controls;

/// <summary>
/// Status bar control showing terminal session statistics.
/// </summary>
public partial class TerminalStatusBar : UserControl
{
    public static readonly DependencyProperty StatsProperty =
        DependencyProperty.Register(
            nameof(Stats),
            typeof(TerminalStats),
            typeof(TerminalStatusBar),
            new PropertyMetadata(null, OnStatsChanged));

    public TerminalStats? Stats
    {
        get => (TerminalStats?)GetValue(StatsProperty);
        set => SetValue(StatsProperty, value);
    }

    public TerminalStatusBar()
    {
        InitializeComponent();
    }

    private static void OnStatsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TerminalStatusBar statusBar)
        {
            statusBar.UpdateDisplay();
        }
    }

    /// <summary>
    /// Updates the display with current stats values.
    /// </summary>
    public void UpdateDisplay()
    {
        var stats = Stats;
        if (stats == null)
        {
            UptimeText.Text = "--:--:--";
            LatencyText.Text = "--ms";
            CpuText.Text = "--%";
            MemText.Text = "--%";
            DiskText.Text = "--%";
            ServerUptimeText.Text = "--";
            UploadText.Text = "0 B/s";
            DownloadText.Text = "0 B/s";
            return;
        }

        // Uptime
        UptimeText.Text = FormatUptime(stats.Uptime);

        // Latency
        LatencyText.Text = stats.Latency.HasValue
            ? $"{stats.Latency.Value.TotalMilliseconds:F0}ms"
            : "--ms";

        // CPU
        CpuText.Text = stats.CpuUsage.HasValue
            ? $"{stats.CpuUsage.Value:F1}%"
            : "--%";

        // Memory
        MemText.Text = stats.MemoryUsage.HasValue
            ? $"{stats.MemoryUsage.Value:F1}%"
            : "--%";

        // Disk
        DiskText.Text = stats.DiskUsage.HasValue
            ? $"{stats.DiskUsage.Value:F0}%"
            : "--%";

        // Server Uptime
        ServerUptimeText.Text = stats.ServerUptime.HasValue
            ? FormatServerUptime(stats.ServerUptime.Value)
            : "--";

        // Throughput
        UploadText.Text = TerminalStats.FormatThroughput(stats.BytesSentPerSecond);
        DownloadText.Text = TerminalStats.FormatThroughput(stats.BytesReceivedPerSecond);
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
        {
            return $"{(int)uptime.TotalDays}d {uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";
        }
        return $"{(int)uptime.TotalHours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";
    }

    private static string FormatServerUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
        {
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h";
        }
        if (uptime.TotalHours >= 1)
        {
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        }
        return $"{uptime.Minutes}m {uptime.Seconds}s";
    }
}
