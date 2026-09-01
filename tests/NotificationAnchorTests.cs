using System.Text.Json;
using AchievementOverlay;
using Xunit;

namespace AchievementOverlay.Tests;

public class NotificationAnchorTests
{
    [Fact]
    public void Default_IsBottomRight()
    {
        // No existing config carries the key, so the absent case has to be where the popup has always
        // gone. That is the enum's member 0, not a value anything assigns.
        Assert.Equal(NotificationAnchor.BottomRight, default(NotificationAnchor));
    }

    [Theory]
    [InlineData("top_left", NotificationAnchor.TopLeft)]
    [InlineData("top_center", NotificationAnchor.TopCenter)]
    [InlineData("top_right", NotificationAnchor.TopRight)]
    [InlineData("bot_left", NotificationAnchor.BottomLeft)]
    [InlineData("bot_center", NotificationAnchor.BottomCenter)]
    [InlineData("bot_right", NotificationAnchor.BottomRight)]
    public void Parse_ReadsGbesOwnSpellings(string text, NotificationAnchor expected)
    {
        Assert.Equal(expected, NotificationAnchors.Parse(text));
    }

    [Theory]
    [InlineData("TOP_RIGHT")]
    [InlineData(" top-right ")]
    [InlineData("Top Right")]
    [InlineData("topright")]
    [InlineData("bottom_right")] // 'bottom' spelled out, where GBE writes 'bot'
    public void Parse_IgnoresCaseAndSeparators(string text)
    {
        var expected = text.Contains("bot", StringComparison.OrdinalIgnoreCase)
            ? NotificationAnchor.BottomRight
            : NotificationAnchor.TopRight;
        Assert.Equal(expected, NotificationAnchors.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("middle")]
    [InlineData("42")]
    [InlineData("mid_left")]
    public void Parse_UnrecognisedReadsAsBottomRight(string? text)
    {
        // A hand-edited value must cost the popup's position, never the app's startup.
        Assert.Equal(NotificationAnchor.BottomRight, NotificationAnchors.Parse(text));
    }

    [Fact]
    public void ToConfigString_RoundTripsEveryMember()
    {
        foreach (var anchor in Enum.GetValues<NotificationAnchor>())
            Assert.Equal(anchor, NotificationAnchors.Parse(anchor.ToConfigString()));
    }

    [Fact]
    public void Serializes_AsGbesStringNotAsANumber()
    {
        // The converter is on the type, so it applies even when the value is serialized on its own —
        // which is what AppConfig.UpdateConfigValues does when the settings window saves. A bare enum
        // would write 3, which nobody reading config.json can act on.
        Assert.Equal("\"top_right\"", JsonSerializer.Serialize(NotificationAnchor.TopRight));
    }

    [Theory]
    [InlineData("\"top_left\"", NotificationAnchor.TopLeft)]
    [InlineData("\"sideways\"", NotificationAnchor.BottomRight)]
    [InlineData("null", NotificationAnchor.BottomRight)]
    [InlineData("3", NotificationAnchor.BottomRight)]
    [InlineData("true", NotificationAnchor.BottomRight)]
    [InlineData("{}", NotificationAnchor.BottomRight)]
    [InlineData("[1,2]", NotificationAnchor.BottomRight)]
    public void Deserializes_WithoutThrowingOnAnyToken(string json, NotificationAnchor expected)
    {
        // Deliberately unlike NotificationScaleConverter: a throw here would escape AppConfig's load
        // and put the config-error dialog in front of a user who mistyped a corner.
        Assert.Equal(expected, JsonSerializer.Deserialize<NotificationAnchor>(json));
    }
}
