using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RJCP.IO.Ports;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Controls;

/// <summary>
/// Status bar control showing terminal session statistics.
/// Supports both SSH and Serial connection types with distinct displays.
/// </summary>
public partial class TerminalStatusBar : UserControl
{
    public static readonly DependencyProperty StatsProperty =
        DependencyProperty.Register(
            nameof(Stats),
            typeof(TerminalStats),
            typeof(TerminalStatusBar),
            new PropertyMetadata(null, OnStatsChanged));

    public static readonly DependencyProperty IsSerialSessionProperty =
        DependencyProperty.Register(
            nameof(IsSerialSession),
            typeof(bool),
            typeof(TerminalStatusBar),
            new PropertyMetadata(false, OnIsSerialSessionChanged));

    public static readonly DependencyProperty SerialConnectionInfoProperty =
        DependencyProperty.Register(
            nameof(SerialConnectionInfo),
            typeof(SerialConnectionInfo),
            typeof(TerminalStatusBar),
            new PropertyMetadata(null, OnSerialConnectionInfoChanged));

    public TerminalStats? Stats
    {
        get => (TerminalStats?)GetValue(StatsProperty);
        set => SetValue(StatsProperty, value);
    }

    /// <summary>
    /// Gets or sets whether this is a serial session (vs SSH session).
    /// </summary>
    public bool IsSerialSession
    {
        get => (bool)GetValue(IsSerialSessionProperty);
        set => SetValue(IsSerialSessionProperty, value);
    }

    /// <summary>
    /// Gets or sets the serial connection info for display.
    /// </summary>
    public SerialConnectionInfo? SerialConnectionInfo
    {
        get => (SerialConnectionInfo?)GetValue(SerialConnectionInfoProperty);
        set => SetValue(SerialConnectionInfoProperty, value);
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

    private static void OnIsSerialSessionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TerminalStatusBar statusBar)
        {
            statusBar.UpdateStatusBarVisibility();
        }
    }

    private static void OnSerialConnectionInfoChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TerminalStatusBar statusBar)
        {
            statusBar.UpdateSerialDisplay();
        }
    }

    /// <summary>
    /// Updates which status bar grid is visible based on connection type.
    /// </summary>
    private void UpdateStatusBarVisibility()
    {
        if (IsSerialSession)
        {
            SshStatusGrid.Visibility = Visibility.Collapsed;
            SerialStatusGrid.Visibility = Visibility.Visible;
        }
        else
        {
            SshStatusGrid.Visibility = Visibility.Visible;
            SerialStatusGrid.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Updates the display with current stats values.
    /// </summary>
    public void UpdateDisplay()
    {
        var stats = Stats;

        if (IsSerialSession)
        {
            UpdateSerialStatsDisplay(stats);
        }
        else
        {
            UpdateSshStatsDisplay(stats);
        }
    }

    /// <summary>
    /// Updates the SSH status bar display.
    /// </summary>
    private void UpdateSshStatsDisplay(TerminalStats? stats)
    {
        if (stats == null)
        {
            UptimeText.Text = "--:--:--";
            LatencyText.Text = "--ms";
            LatencyText.Foreground = DefaultBrush;
            CpuText.Text = "--%";
            CpuText.Foreground = DefaultBrush;
            MemText.Text = "--%";
            MemText.Foreground = DefaultBrush;
            DiskText.Text = "--%";
            DiskText.Foreground = DefaultBrush;
            ServerUptimeText.Text = "--";
            UploadText.Text = "0 B/s";
            DownloadText.Text = "0 B/s";
            return;
        }

        // Uptime
        UptimeText.Text = FormatUptime(stats.Uptime);

        // Latency (with color)
        if (stats.Latency.HasValue)
        {
            var latencyMs = stats.Latency.Value.TotalMilliseconds;
            LatencyText.Text = $"{latencyMs:F0}ms";
            LatencyText.Foreground = GetLatencyColorBrush(latencyMs);
        }
        else
        {
            LatencyText.Text = "--ms";
            LatencyText.Foreground = DefaultBrush;
        }

        // CPU (with color)
        if (stats.CpuUsage.HasValue)
        {
            CpuText.Text = $"{stats.CpuUsage.Value:F1}%";
            CpuText.Foreground = GetUsageColorBrush(stats.CpuUsage.Value);
        }
        else
        {
            CpuText.Text = "--%";
            CpuText.Foreground = DefaultBrush;
        }

        // Memory (with color)
        if (stats.MemoryUsage.HasValue)
        {
            MemText.Text = $"{stats.MemoryUsage.Value:F1}%";
            MemText.Foreground = GetUsageColorBrush(stats.MemoryUsage.Value);
        }
        else
        {
            MemText.Text = "--%";
            MemText.Foreground = DefaultBrush;
        }

        // Disk (with color)
        if (stats.DiskUsage.HasValue)
        {
            DiskText.Text = $"{stats.DiskUsage.Value:F0}%";
            DiskText.Foreground = GetUsageColorBrush(stats.DiskUsage.Value);
        }
        else
        {
            DiskText.Text = "--%";
            DiskText.Foreground = DefaultBrush;
        }

        // Server Uptime
        ServerUptimeText.Text = stats.ServerUptime.HasValue
            ? FormatServerUptime(stats.ServerUptime.Value)
            : "--";

        // Throughput
        UploadText.Text = TerminalStats.FormatThroughput(stats.BytesSentPerSecond);
        DownloadText.Text = TerminalStats.FormatThroughput(stats.BytesReceivedPerSecond);
    }

    /// <summary>
    /// Updates the serial status bar with connection info.
    /// </summary>
    private void UpdateSerialDisplay()
    {
        var info = SerialConnectionInfo;
        if (info == null)
        {
            SerialPortText.Text = "--";
            SerialBaudText.Text = "--";
            SerialSettingsText.Text = "--";
            SerialHandshakeText.Text = "--";
            return;
        }

        // Port name
        SerialPortText.Text = info.PortName;

        // Baud rate
        SerialBaudText.Text = info.BaudRate.ToString();

        // Settings (Data bits, Parity, Stop bits) - e.g., "8N1"
        SerialSettingsText.Text = GetSerialSettingsString(info);

        // Handshake / Flow control
        SerialHandshakeText.Text = GetHandshakeString(info.Handshake);
    }

    /// <summary>
    /// Updates the serial status bar with stats (uptime, throughput).
    /// </summary>
    private void UpdateSerialStatsDisplay(TerminalStats? stats)
    {
        if (stats == null)
        {
            SerialUptimeText.Text = "--:--:--";
            SerialTxText.Text = "0 B/s";
            SerialRxText.Text = "0 B/s";
            return;
        }

        // Session uptime
        SerialUptimeText.Text = FormatUptime(stats.Uptime);

        // Throughput (TX = sent, RX = received)
        SerialTxText.Text = TerminalStats.FormatThroughput(stats.BytesSentPerSecond);
        SerialRxText.Text = TerminalStats.FormatThroughput(stats.BytesReceivedPerSecond);
    }

    /// <summary>
    /// Gets a compact string representing serial settings (e.g., "8N1").
    /// </summary>
    /// <param name="info">The serial connection info.</param>
    /// <returns>A string like "8N1" for 8 data bits, No parity, 1 stop bit.</returns>
    private static string GetSerialSettingsString(SerialConnectionInfo info)
    {
        var parityChar = info.Parity switch
        {
            Parity.None => 'N',
            Parity.Odd => 'O',
            Parity.Even => 'E',
            Parity.Mark => 'M',
            Parity.Space => 'S',
            _ => '?'
        };

        var stopBitsStr = info.StopBits switch
        {
            StopBits.One => "1",
            StopBits.One5 => "1.5",
            StopBits.Two => "2",
            _ => "?"
        };

        return $"{info.DataBits}{parityChar}{stopBitsStr}";
    }

    /// <summary>
    /// Gets a display string for handshake/flow control mode.
    /// </summary>
    private static string GetHandshakeString(Handshake handshake)
    {
        return handshake switch
        {
            Handshake.None => "None",
            Handshake.XOn => "XON/XOFF",
            Handshake.Rts => "RTS/CTS",
            Handshake.RtsXOn => "RTS+XON",
            _ => "?"
        };
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

    // Color constants for status indicators
    private static readonly SolidColorBrush GreenBrush = new(Color.FromRgb(0x4C, 0xAF, 0x50));   // #4CAF50 - Good
    private static readonly SolidColorBrush YellowBrush = new(Color.FromRgb(0xFF, 0xC1, 0x07));  // #FFC107 - Warning
    private static readonly SolidColorBrush OrangeBrush = new(Color.FromRgb(0xFF, 0x98, 0x00));  // #FF9800 - Caution
    private static readonly SolidColorBrush RedBrush = new(Color.FromRgb(0xF4, 0x43, 0x36));     // #F44336 - Critical
    private static readonly SolidColorBrush DefaultBrush = new(Color.FromRgb(0xCC, 0xCC, 0xCC)); // #CCCCCC - Default

    /// <summary>
    /// Gets a color brush based on a percentage value (higher = worse).
    /// Used for CPU, Memory, and Disk usage.
    /// </summary>
    private static SolidColorBrush GetUsageColorBrush(double percentage)
    {
        return percentage switch
        {
            >= 90 => RedBrush,
            >= 75 => OrangeBrush,
            >= 50 => YellowBrush,
            _ => GreenBrush
        };
    }

    /// <summary>
    /// Gets a color brush based on latency in milliseconds (lower = better).
    /// </summary>
    private static SolidColorBrush GetLatencyColorBrush(double milliseconds)
    {
        return milliseconds switch
        {
            >= 500 => RedBrush,
            >= 200 => OrangeBrush,
            >= 100 => YellowBrush,
            _ => GreenBrush
        };
    }
}
