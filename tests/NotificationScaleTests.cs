using AchievementOverlay;
using Xunit;

namespace AchievementOverlay.Tests;

public class NotificationScaleTests
{
    [Theory]
    [InlineData("15%", 15)]
    [InlineData(" 22.5 % ", 22.5)]
    public void Parse_Percentage_IsAShareOfTheScreen(string text, double expected)
    {
        var scale = NotificationScale.Parse(text);

        Assert.Equal(ScaleUnit.ScreenPercent, scale.Unit);
        Assert.Equal(expected, scale.Value);
    }

    [Theory]
    [InlineData("384px", 384)]
    [InlineData("384", 384)]
    public void Parse_Pixels_IsAnAbsoluteWidth(string text, double expected)
    {
        var scale = NotificationScale.Parse(text);

        Assert.Equal(ScaleUnit.Pixels, scale.Unit);
        Assert.Equal(expected, scale.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("enormous")]
    public void Parse_UnusableValue_FallsBackToTheDefault(string? text)
    {
        // A hand-edited value must cost at most the popup's size, never the app's startup.
        Assert.Equal(NotificationScale.Default, NotificationScale.Parse(text));
    }

    [Theory]
    [InlineData("15%")]
    [InlineData("384px")]
    [InlineData("22.5%")]
    public void ToString_RoundTripsThroughParse(string written)
    {
        // The written form has to read back as the same setting, or saving changes the value.
        Assert.Equal(written.Replace(" ", ""), NotificationScale.Parse(written).ToString());
    }

    [Fact]
    public void WidthOn_ScreenPercent_TracksTheDisplay()
    {
        var scale = NotificationScale.ScreenPercent(15);

        Assert.Equal(384, scale.WidthOn(2560), 3);
        Assert.Equal(288, scale.WidthOn(1920), 3);
    }

    [Fact]
    public void WidthOn_Pixels_IgnoresTheDisplay()
    {
        var scale = NotificationScale.Pixels(384);

        Assert.Equal(384, scale.WidthOn(2560), 3);
        Assert.Equal(384, scale.WidthOn(1920), 3);
    }

    [Fact]
    public void ComputeWidth_ScreenPercent_MatchesTheOldAutomaticBehaviour()
    {
        // Automatic was 15% of the display width, so the default has to still produce exactly that.
        Assert.Equal(384, NotificationWindow.ComputeWidth(NotificationScale.Default, 2560), 1);
    }

    [Fact]
    public void ComputeWidth_Pixels_IsTheRequestedWidth()
    {
        Assert.Equal(500, NotificationWindow.ComputeWidth(NotificationScale.Pixels(500), 2560), 1);
    }

    [Fact]
    public void ComputeWidth_DefaultOn1080p_IsNeverBelowTheDesignWidth()
    {
        // 15% of 1920 is 288 px against a 322 px design width. Drawing that shrank the title to
        // 12.5 px and the description to 10.7 px, which is the "reads very small" complaint.
        Assert.Equal(NotificationScale.DesignWidth, NotificationWindow.ComputeWidth(NotificationScale.Default, 1920), 1);
    }

    [Theory]
    [InlineData(1920)]
    [InlineData(1366)]
    [InlineData(960)]   // 1080p at 200% display scaling
    public void ComputeScale_NarrowDisplays_NeverDrawSmallerThanDesigned(double displayWidth)
    {
        Assert.True(NotificationWindow.ComputeScale(NotificationScale.Default, displayWidth) >= 1.0);
    }

    [Theory]
    [InlineData(5, 200)]      // far too small on a small screen
    [InlineData(50, 100000)]  // absurd on a huge one
    public void ComputeWidth_StaysWithinTheReadabilityClamp(double percent, double displayWidth)
    {
        var width = NotificationWindow.ComputeWidth(NotificationScale.ScreenPercent(percent), displayWidth);

        Assert.InRange(width,
            NotificationScale.DesignWidth * NotificationScale.MinFactor,
            NotificationScale.DesignWidth * NotificationScale.MaxFactor);
    }
}
