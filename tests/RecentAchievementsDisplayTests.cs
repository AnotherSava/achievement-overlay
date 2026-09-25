using System.Globalization;
using Xunit;

namespace AchievementOverlay.Tests;

public class RecentAchievementsDisplayTests
{
    private static readonly TimeZoneInfo UtcPlus3 = TimeZoneInfo.CreateCustomTimeZone("UTC+03", TimeSpan.FromHours(3), "UTC+03", "UTC+03");
    private static readonly TimeZoneInfo UtcMinus5 = TimeZoneInfo.CreateCustomTimeZone("UTC-05", TimeSpan.FromHours(-5), "UTC-05", "UTC-05");

    [Fact]
    public void LocalEarnedTime_InRange_IsTheClockTimeInTheZone()
    {
        Assert.Equal(new DateTime(2023, 11, 14, 22, 13, 20), RecentAchievementsDisplay.LocalEarnedTime(1_700_000_000, TimeZoneInfo.Utc, CultureInfo.InvariantCulture));
        Assert.Equal(new DateTime(2023, 11, 15, 1, 13, 20), RecentAchievementsDisplay.LocalEarnedTime(1_700_000_000, UtcPlus3, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void LocalEarnedTime_EpochMilliseconds_IsUnknown() => Assert.Null(RecentAchievementsDisplay.LocalEarnedTime(1_700_000_000_123, TimeZoneInfo.Utc, CultureInfo.InvariantCulture));

    [Fact]
    public void LocalEarnedTime_TheLastSecondADateCanHold_IsKnown() => Assert.Equal(new DateTime(9999, 12, 31, 23, 59, 59), RecentAchievementsDisplay.LocalEarnedTime(253_402_300_799, TimeZoneInfo.Utc, CultureInfo.InvariantCulture));

    [Fact]
    public void LocalEarnedTime_TheFirstSecondADateCanHold_IsKnown() => Assert.Equal(DateTime.MinValue, RecentAchievementsDisplay.LocalEarnedTime(-62_135_596_800, TimeZoneInfo.Utc, CultureInfo.InvariantCulture));

    [Theory]
    [InlineData(253_402_300_800)]
    [InlineData(-62_135_596_801)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void LocalEarnedTime_PastEitherEnd_IsUnknown(long earnedTime) => Assert.Null(RecentAchievementsDisplay.LocalEarnedTime(earnedTime, TimeZoneInfo.Utc, CultureInfo.InvariantCulture));

    [Fact]
    public void LocalEarnedTime_InRangeInUtcButNotInTheZone_IsUnknownRatherThanClamped()
    {
        // Three hours east of UTC the last representable second reads as year 10000, and five hours
        // west the first one reads as year 0; neither is a clock time, so neither becomes one.
        Assert.Null(RecentAchievementsDisplay.LocalEarnedTime(253_402_300_799, UtcPlus3, CultureInfo.InvariantCulture));
        Assert.Null(RecentAchievementsDisplay.LocalEarnedTime(-62_135_596_800, UtcMinus5, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void LocalEarnedTime_PastTheDisplayCalendarsRange_IsUnknown()
    {
        // ar-SA formats through the Um al-Qura calendar, which ends in 2077; a 2100 date is a DateTime
        // but not a date that culture can write.
        var arabic = CultureInfo.GetCultureInfo("ar-SA");
        Assert.Null(RecentAchievementsDisplay.LocalEarnedTime(4_102_444_800, TimeZoneInfo.Utc, arabic));
        Assert.NotNull(RecentAchievementsDisplay.LocalEarnedTime(1_700_000_000, TimeZoneInfo.Utc, arabic));
    }
}
