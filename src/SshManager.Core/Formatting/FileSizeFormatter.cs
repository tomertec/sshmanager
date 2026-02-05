namespace SshManager.Core.Formatting;

/// <summary>
/// Shared utility for formatting file sizes and transfer speeds.
/// </summary>
public static class FileSizeFormatter
{
    /// <summary>
    /// Formats a byte count as a human-readable file size (e.g., "1.5 MB").
    /// </summary>
    public static string FormatSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int suffixIndex = 0;
        double size = bytes;

        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return suffixIndex == 0
            ? $"{size:N0} {suffixes[suffixIndex]}"
            : $"{size:N1} {suffixes[suffixIndex]}";
    }

    /// <summary>
    /// Formats a transfer speed in bytes per second (e.g., "1.5 MB/s").
    /// </summary>
    public static string FormatSpeed(double bytesPerSecond)
    {
        return bytesPerSecond switch
        {
            >= 1_073_741_824 => $"{bytesPerSecond / 1_073_741_824:F1} GB/s",
            >= 1_048_576 => $"{bytesPerSecond / 1_048_576:F1} MB/s",
            >= 1_024 => $"{bytesPerSecond / 1_024:F1} KB/s",
            _ => $"{bytesPerSecond:F0} B/s"
        };
    }
}
