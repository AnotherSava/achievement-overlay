using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace AchievementOverlay.Tests;

[Collection("App log")]
public class LoggerTests
{
    // th-TH counts years in the Buddhist era (2569) and fi-FI separates the time with dots (02.00.00).
    // The first dates every log line 543 years ahead; the second is a stamp DiagnosticReport's
    // LineTimestamp does not recognise, so it stays in the comparison and a repeated run no longer folds.
    [Theory]
    [InlineData("th-TH")]
    [InlineData("fi-FI")]
    public void InitAndInfo_UnderACultureWithItsOwnCalendarOrTimeSeparator_StampInTheInvariantForm(string culture)
    {
        var marker = "logger-culture-" + Guid.NewGuid().ToString("N");
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            Logger.Init();
            Logger.Info(marker);
        }
        finally
        {
            Logger.Close();
            CultureInfo.CurrentCulture = previous;
        }

        var log = DiagnosticFile.Read(Logger.LogPath);
        Assert.Equal("ok", log.Status);
        Assert.NotNull(log.Content);
        var lines = DiagnosticReport.SplitLines(log.Content).ToList();
        var markerIndex = lines.FindIndex(l => l.Contains(marker, StringComparison.Ordinal));
        Assert.True(markerIndex >= 0, $"No line in {Logger.LogPath} carries {marker}");
        Assert.Matches(@"^\[20\d\d-\d\d-\d\d \d\d:\d\d:\d\d\] \[INFO\] ", lines[markerIndex]);

        // Nothing else in the test run opens the log, so the nearest banner above the marker is this Init's.
        var banner = lines.Take(markerIndex).LastOrDefault(l => l.StartsWith(Logger.SessionBannerPrefix, StringComparison.Ordinal));
        Assert.NotNull(banner);
        Assert.Matches($@"^{Regex.Escape(Logger.SessionBannerPrefix)} 20\d\d-\d\d-\d\d \d\d:\d\d:\d\d, ", banner);
    }
}
