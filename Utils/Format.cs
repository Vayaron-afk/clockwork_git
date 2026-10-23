using System.Globalization;

namespace ClockworkGit.Utils;

public static class Format
{
    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.##} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} B",
    };

    /// <summary>SmartGit-style dates: "12:49", "Yesterday 21:13", "Wednesday 19:28", then the full date.</summary>
    public static string RelativeDate(DateTimeOffset date)
    {
        var local = date.LocalDateTime;
        var today = DateTime.Today;
        if (local.Date == today) return local.ToString("HH:mm");
        if (local.Date == today.AddDays(-1)) return "Yesterday " + local.ToString("HH:mm");
        if ((today - local.Date).TotalDays < 7) return local.ToString("dddd HH:mm", CultureInfo.InvariantCulture);
        return local.ToString("dd.MM.yyyy HH:mm");
    }
}
