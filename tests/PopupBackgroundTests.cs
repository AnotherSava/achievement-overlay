using System.Text.Json;
using AchievementOverlay;
using Xunit;
using Color = System.Windows.Media.Color;

namespace AchievementOverlay.Tests;

public class PopupBackgroundTests
{
    [Fact]
    public void Default_IsTheShippedFill()
    {
        Assert.Equal("#DD1A1A2E", PopupBackground.Default.ToString());
        Assert.Equal(Color.FromArgb(0xDD, 0x1A, 0x1A, 0x2E), PopupBackground.Default.ToColor());
    }

    [Theory]
    [InlineData("#DD1A1A2E", "#DD1A1A2E")]
    [InlineData("dd1a1a2e", "#DD1A1A2E")]     // the '#' is optional
    [InlineData(" #F00 ", "#DDFF0000")]        // 3 digits: doubled, and takes the default's alpha
    [InlineData("#8F00", "#88FF0000")]         // 4 digits: doubled, alpha first
    [InlineData("#1A1A2E", "#DD1A1A2E")]       // 6 digits: takes the default's alpha
    public void Parse_ReadsEveryHexForm(string text, string expected)
    {
        Assert.Equal(expected, PopupBackground.Parse(text).ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("red")]
    [InlineData("sc#1,1,1,1")]
    [InlineData("ContextColor file://x 1,1,1,1")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("#")]
    public void Parse_UnreadableValuesFallBackWithoutThrowing(string? text)
    {
        // WPF's own ColorConverter throws three different exception types across these inputs, which is
        // why this parser is hand-written.
        Assert.Equal(PopupBackground.Default, PopupBackground.Parse(text));
    }

    [Fact]
    public void Parse_ClampsAlphaToTheVisibleFloor()
    {
        // A popup at 0% alpha is reported as "notifications stopped working", never as a bad colour.
        Assert.Equal(PopupBackground.MinAlpha, PopupBackground.Parse("#001A1A2E").A);
        Assert.Equal(PopupBackground.MinAlpha, PopupBackground.From(0x10, 1, 2, 3).A);
        Assert.Equal(0xFF, PopupBackground.From(0xFF, 1, 2, 3).A);
    }

    [Fact]
    public void ToString_RoundTrips()
    {
        foreach (var value in new[] { PopupBackground.Default, PopupBackground.From(0x66, 0, 0, 0), PopupBackground.From(0xFF, 0xF5, 0xF5, 0xF0) })
            Assert.Equal(value, PopupBackground.Parse(value.ToString()));
    }

    [Fact]
    public void WithAlphaAndWithColour_ChangeOneHalfEach()
    {
        var value = PopupBackground.Default.WithAlpha(0xF0);
        Assert.Equal(0xF0, value.A);
        Assert.True(value.IsColour(Color.FromRgb(0x1A, 0x1A, 0x2E)));

        var recoloured = value.WithColour(Color.FromRgb(0xF5, 0xF5, 0xF0));
        Assert.Equal(0xF0, recoloured.A);
        Assert.True(recoloured.IsColour(Color.FromRgb(0xF5, 0xF5, 0xF0)));
    }

    [Fact]
    public void Serializes_AsItsStringFormNotAsAnObject()
    {
        // The settings window boxes this into an object dictionary, so only a type-level converter
        // applies — the trap the scale setting already fell into once.
        Assert.Equal("\"#DD1A1A2E\"", JsonSerializer.Serialize(PopupBackground.Default));
    }

    [Theory]
    [InlineData("\"#FF102030\"", "#FF102030")]
    [InlineData("\"nonsense\"", "#DD1A1A2E")]
    [InlineData("null", "#DD1A1A2E")]
    [InlineData("17", "#DD1A1A2E")]
    [InlineData("true", "#DD1A1A2E")]
    [InlineData("{}", "#DD1A1A2E")]
    [InlineData("[1,2]", "#DD1A1A2E")]
    public void Deserializes_WithoutThrowingOnAnyToken(string json, string expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<PopupBackground>(json).ToString());
    }
}
