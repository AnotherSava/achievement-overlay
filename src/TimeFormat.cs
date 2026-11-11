using System.Globalization;

namespace AchievementOverlay;

/// <summary>Time as the app's lists show it.</summary>
public static class TimeFormat
{
    /// <summary>
    /// A compact interval: the largest whole unit of m, h, d, mo (30 days) or y (365 days), never below 1m,
    /// so a clock a little out of step reads 1m rather than 0m or a negative. Bare magnitude; the caller adds
    /// "ago". Months are "mo" so a column never tells minutes from months by one letter's case.
    /// </summary>
    public static string Interval(TimeSpan span)
    {
        var minutes = Math.Max(1L, (long)Math.Floor(span.TotalMinutes));
        if (minutes < 60)
            return Format(minutes, "m");
        var hours = minutes / 60;
        if (hours < 24)
            return Format(hours, "h");
        var days = hours / 24;
        if (days < 30)
            return Format(days, "d");
        return days < 365 ? Format(days / 30, "mo") : Format(days / 365, "y");

        static string Format(long value, string unit) => value.ToString(CultureInfo.CurrentCulture) + unit;
    }
}
