using AchievementOverlay.GbeOverlay;
using Xunit;

namespace AchievementOverlay.Tests.GbeOverlay;

public class GameOverlayConfigTests
{
    private static GameOverlayConfig Parse(string body) =>
        GameOverlayConfig.Parse(IniFile.Parse("[overlay::appearance]\n" + body));

    [Fact]
    public void Parse_Duration_ReadsSeconds()
    {
        Assert.Equal(12.0, Parse("Notification_Duration_Achievement=12.0\n").AchievementDurationSeconds);
    }

    [Fact]
    public void Parse_DurationWithTrailingText_TakesTheNumericPrefix()
    {
        // GBE reads these with std::stof, which stops at the first character it can't use.
        Assert.Equal(7.0, Parse("Notification_Duration_Achievement=7.0s\n").AchievementDurationSeconds);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.0")]
    [InlineData("-3")]
    public void Parse_ZeroOrNegativeDuration_LeavesTheKeyUnset(string value)
    {
        // GBE suppresses the popup entirely for these; deliberately not reproduced, or one game's
        // stale ini would silently stop this app from notifying at all.
        Assert.Null(Parse($"Notification_Duration_Achievement={value}\n").AchievementDurationSeconds);
    }

    [Fact]
    public void Parse_UnparseableDuration_LeavesTheKeyUnsetAndKeepsTheNext()
    {
        var config = Parse("Notification_Duration_Achievement=soon\nFont_Override=poppins.ttf\n");

        Assert.Null(config.AchievementDurationSeconds);
        Assert.Equal("poppins.ttf", config.FontOverride);
    }

    [Fact]
    public void Parse_FontOverride_IsReadAsWritten()
    {
        Assert.Equal("poppins.ttf", Parse("Font_Override=  poppins.ttf  \n").FontOverride);
    }

    [Fact]
    public void Parse_KeysInAnotherConfigFileSection_AreStillFound()
    {
        // The section decides meaning, not the file name: [overlay::appearance] counts wherever it is.
        var ini = IniFile.Parse("[overlay::general]\nenable_experimental_overlay=0\n")
            .WithFallback(IniFile.Parse("[overlay::appearance]\nFont_Override=poppins.ttf\n"));

        Assert.Equal("poppins.ttf", GameOverlayConfig.Parse(ini).FontOverride);
    }

    [Fact]
    public void Parse_NothingRecognised_LeavesEveryKeyUnset()
    {
        var config = GameOverlayConfig.Parse(IniFile.Parse("[overlay::general]\nenable_experimental_overlay=0\n"));

        Assert.Null(config.AchievementDurationSeconds);
        Assert.Null(config.FontOverride);
    }

    [Theory]
    [InlineData("7", 7.0)]
    [InlineData("7.5", 7.5)]
    [InlineData("-2.5", -2.5)]
    [InlineData("+3", 3.0)]
    [InlineData("12abc", 12.0)]
    [InlineData("abc", null)]
    [InlineData("", null)]
    [InlineData(".", null)]
    public void ParseLeadingDouble_MatchesStofSemantics(string text, double? expected)
    {
        Assert.Equal(expected, GameOverlayConfig.ParseLeadingDouble(text));
    }
}
