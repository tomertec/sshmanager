using System.Globalization;
using System.Windows.Data;

namespace SshManager.App.Converters;

/// <summary>
/// Converts a DateTimeOffset to a relative time string (e.g., "5 minutes ago", "2 hours ago").
/// </summary>
public class RelativeTimeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTimeOffset dateTime)
            return null;

        var now = DateTimeOffset.UtcNow;
        var diff = now - dateTime;

        if (diff.TotalSeconds < 60)
            return "Just now";

        if (diff.TotalMinutes < 60)
        {
            var minutes = (int)diff.TotalMinutes;
            return minutes == 1 ? "1 minute ago" : $"{minutes} minutes ago";
        }

        if (diff.TotalHours < 24)
        {
            var hours = (int)diff.TotalHours;
            return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
        }

        if (diff.TotalDays < 7)
        {
            var days = (int)diff.TotalDays;
            return days == 1 ? "Yesterday" : $"{days} days ago";
        }

        if (diff.TotalDays < 30)
        {
            var weeks = (int)(diff.TotalDays / 7);
            return weeks == 1 ? "1 week ago" : $"{weeks} weeks ago";
        }

        if (diff.TotalDays < 365)
        {
            var months = (int)(diff.TotalDays / 30);
            return months == 1 ? "1 month ago" : $"{months} months ago";
        }

        var years = (int)(diff.TotalDays / 365);
        return years == 1 ? "1 year ago" : $"{years} years ago";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
