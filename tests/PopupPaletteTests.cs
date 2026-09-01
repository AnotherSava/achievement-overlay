using AchievementOverlay;
using Xunit;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace AchievementOverlay.Tests;

/// <summary>
/// The contrast maths is re-implemented here rather than exposed from the palette, so these assert
/// against an independent reading of WCAG rather than against the production formula's own opinion of
/// itself.
/// </summary>
public class PopupPaletteTests
{
    private static readonly Color ShippedFill = Color.FromArgb(0xDD, 0x1A, 0x1A, 0x2E);

    [Fact]
    public void For_TheShippedFill_ReturnsExactlyTodaysColours()
    {
        // The most valuable assertion in the change: if this fails, the default popup's look has
        // shifted for every existing user. The four expected values are the literals that were in
        // NotificationWindow.xaml and ShowFooter before the palette replaced them.
        var palette = PopupPalette.For(ShippedFill);

        Assert.Equal(ShippedFill, palette.Background);
        Assert.Equal(Colors.White, palette.Title);
        Assert.Equal(Color.FromArgb(0xCC, 0xAA, 0xAA, 0xAA), palette.Description);
        Assert.Equal(Color.FromArgb(0x99, 0xAA, 0xAA, 0xAA), palette.GameLine);
        Assert.Equal(Color.FromArgb(0xBB, 0xFF, 0xFF, 0xFF), palette.Footer);
    }

    [Fact]
    public void For_KeepsTheGoldIconRingWhereverItStillReads()
    {
        // The fallback trophy's ring is gold on the shipped fill, as it has always been.
        Assert.Equal(Color.FromRgb(0xFF, 0xD7, 0x00), PopupPalette.For(ShippedFill).IconRing);
        Assert.Equal(Color.FromRgb(0xFF, 0xD7, 0x00), PopupPalette.For(Colors.Black).IconRing);
    }

    [Theory]
    [InlineData(0xF5, 0xF5, 0xF0)] // the light preset
    [InlineData(0xFF, 0xD7, 0x00)] // gold on gold: the disc would have no edge at all
    [InlineData(0xFF, 0xFF, 0xFF)]
    public void For_SwapsTheIconRingForInkWhenGoldWouldVanish(byte r, byte g, byte b)
    {
        var background = Color.FromRgb(r, g, b);
        var ring = PopupPalette.For(background).IconRing;

        Assert.NotEqual(Color.FromRgb(0xFF, 0xD7, 0x00), ring);
        Assert.True(Contrast(ring, background) >= 3.0, "the ring must still separate the disc from the fill");
    }

    [Fact]
    public void For_KeepsEachLevelTranslucent()
    {
        // The alpha is not folded away: the popup's own fill is translucent, so the text is meant to
        // blend with whatever shows through it. Only the *judging* treats the fill as opaque.
        var palette = PopupPalette.For(ShippedFill);

        Assert.True(palette.Description.A < 0xFF);
        Assert.True(palette.GameLine.A < 0xFF);
        Assert.True(palette.Footer.A < 0xFF);
    }

    [Theory]
    [InlineData(0x1A, 0x1A, 0x2E)]
    [InlineData(0x00, 0x00, 0x00)]
    [InlineData(0x20, 0x00, 0x40)]
    public void For_DarkBackgrounds_TakeWhiteInk(byte r, byte g, byte b)
    {
        Assert.Equal(Colors.White, PopupPalette.For(Color.FromRgb(r, g, b)).Title);
    }

    [Theory]
    [InlineData(0xFF, 0xFF, 0xFF)]
    [InlineData(0xFF, 0xFF, 0x00)]
    [InlineData(0xEE, 0xEE, 0xEE)]
    public void For_LightBackgrounds_TakeBlackInk(byte r, byte g, byte b)
    {
        Assert.Equal(Colors.Black, PopupPalette.For(Color.FromRgb(r, g, b)).Title);
    }

    [Fact]
    public void For_FlipsInkEitherSideOfTheCrossover()
    {
        // Walk the grey ramp and find where the ink changes; it must be the luminance where the two
        // are equally readable, not the naive midpoint of the ramp.
        Color? lastDark = null;
        Color? firstLight = null;
        for (var v = 0; v <= 255; v++)
        {
            var grey = Color.FromRgb((byte)v, (byte)v, (byte)v);
            if (PopupPalette.For(grey).Title == Colors.White)
                lastDark = grey;
            else
                firstLight ??= grey;
        }

        Assert.NotNull(lastDark);
        Assert.NotNull(firstLight);
        Assert.True(Luminance(lastDark!.Value) <= PopupPalette.InkCrossover);
        Assert.True(Luminance(firstLight!.Value) > PopupPalette.InkCrossover);
    }

    [Fact]
    public void For_MidGrey_RescuesTheDescriptionToTheFloor()
    {
        // The hard case. A fixed alpha ladder alone puts the description at 1.75:1 here; the rescue
        // lifts it to the floor, and full white — the best anything can do against this background —
        // is only 4.61:1.
        var background = Color.FromRgb(0x75, 0x75, 0x75);
        var palette = PopupPalette.For(background);

        var achieved = Contrast(Over(background, palette.Description), background);
        Assert.True(achieved >= 4.5, $"description reached only {achieved:0.00}:1");
        Assert.True(achieved <= Contrast(Colors.White, background) + 0.001);
    }

    [Fact]
    public void For_DescriptionMeetsAaWhereverItIsReachable()
    {
        foreach (var background in Backgrounds())
        {
            var palette = PopupPalette.For(background);
            var ink = Luminance(background) <= PopupPalette.InkCrossover ? Colors.White : Colors.Black;
            // Where AA is out of reach — near the crossover nothing beats 4.58:1 — the level falls back
            // to full ink, which is the best available rather than a failure.
            var best = Contrast(ink, background);
            var floor = Math.Min(4.5, best);

            var achieved = Contrast(Over(background, palette.Description), background);
            Assert.True(achieved >= floor - 0.01,
                $"#{background.R:X2}{background.G:X2}{background.B:X2}: description {achieved:0.00}:1, needed {floor:0.00}:1");
        }
    }

    [Fact]
    public void For_TitleIsAlwaysTheMostReadableLevel()
    {
        foreach (var background in Backgrounds())
        {
            var palette = PopupPalette.For(background);
            var title = Contrast(Over(background, palette.Title), background);

            Assert.True(title >= Contrast(Over(background, palette.Description), background) - 0.01);
            Assert.True(title >= Contrast(Over(background, palette.GameLine), background) - 0.01);
        }
    }

    private static IEnumerable<Color> Backgrounds()
    {
        for (var v = 0; v <= 255; v += 5)
            yield return Color.FromRgb((byte)v, (byte)v, (byte)v);

        yield return Color.FromRgb(0xFF, 0x00, 0x00);
        yield return Color.FromRgb(0x00, 0xFF, 0x00);
        yield return Color.FromRgb(0x00, 0x00, 0xFF);
        yield return Color.FromRgb(0xFF, 0xFF, 0x00);
        yield return Color.FromRgb(0x00, 0xFF, 0xFF);
        yield return Color.FromRgb(0xFF, 0x00, 0xFF);
        yield return Color.FromRgb(0x1A, 0x1A, 0x2E);
    }

    // --- an independent reading of WCAG ---

    private static Color Over(Color background, Color colour)
    {
        var a = colour.A / 255.0;
        byte Mix(byte front, byte back) => (byte)Math.Round(front * a + back * (1 - a));
        return Color.FromRgb(Mix(colour.R, background.R), Mix(colour.G, background.G), Mix(colour.B, background.B));
    }

    private static double Luminance(Color colour)
    {
        static double Linear(byte value)
        {
            var channel = value / 255.0;
            return channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linear(colour.R) + 0.7152 * Linear(colour.G) + 0.0722 * Linear(colour.B);
    }

    private static double Contrast(Color first, Color second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}
