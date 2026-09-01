using System.Windows.Media;

namespace AchievementOverlay;

/// <summary>
/// Every colour the popup draws its text in, derived from the colour behind them. Pure, so the whole
/// rule is unit-tested without a window.
/// </summary>
/// <remarks>
/// The popup has four foregrounds, and they were tuned by hand for one dark fill. Deriving them is
/// what makes the fill safe to change: a light background flips the ink, and the secondary lines are
/// lifted until they clear a contrast floor. Two limits are accepted rather than engineered around,
/// and both are stated in docs/plans/2026-08-30-popup-position-and-background.md — contrast is judged
/// against the fill as chosen, ignoring its alpha, because what shows through a translucent popup is a
/// game frame nobody can predict; and near <see cref="InkCrossover"/> no ink exceeds 4.58:1, so the
/// game line cannot reach WCAG AA there. It does not reach AA on the shipped default either.
/// </remarks>
public sealed record PopupPalette
{
    /// <summary>
    /// Luminance at which black and white ink are equally readable — √(1.05 × 0.05) − 0.05, where both
    /// give 4.583:1. Deliberately not the naive 0.5 midpoint, which would flip the ink while one side
    /// was still measurably the better of the two.
    /// </summary>
    internal const double InkCrossover = 0.17912878474779200;

    /// <summary>WCAG AA for body text. The description and the recent panel's footer are held to it.</summary>
    private const double BodyFloor = 4.5;

    /// <summary>
    /// What the game line already achieves on the shipped default (3.50:1). Floored at what it has, so
    /// deriving the palette does not silently restyle every existing user's popup to reach a bar the
    /// hand-picked colours never met.
    /// </summary>
    private const double GameLineFloor = 3.4;

    /// <summary>WCAG's floor for a boundary that carries meaning without being text.</summary>
    private const double NonTextFloor = 3.0;

    /// <summary>The fallback icon's own ring colour, kept wherever it still reads against the fill.</summary>
    private static readonly Color IconGold = Color.FromRgb(0xFF, 0xD7, 0x00);

    public required Color Background { get; init; }
    public required Color Title { get; init; }
    public required Color Description { get; init; }
    public required Color GameLine { get; init; }

    /// <summary>The recent panel's dismiss hint, which is set in code rather than in the XAML.</summary>
    public required Color Footer { get; init; }

    /// <summary>
    /// Ring around the fallback trophy icon, drawn when a game supplies no icon file. Gold, unless
    /// gold is too close to the fill to separate the disc from it — on a cream background the whole
    /// trophy would otherwise dissolve. Only the ring adapts: the goldenrod disc and its star are the
    /// icon's identity, and the ring is what makes them legible.
    /// </summary>
    public required Color IconRing { get; init; }

    /// <summary>
    /// Derives the text colours for a background. On the shipped <c>#DD1A1A2E</c> every level clears
    /// its floor untouched, so this returns exactly the four colours the popup has always drawn — the
    /// default look is preserved by construction rather than by promise.
    /// </summary>
    public static PopupPalette For(Color background)
    {
        var ink = RelativeLuminance(background) <= InkCrossover ? Colors.White : Colors.Black;
        // Asymmetric on purpose: a semi-transparent brush blends toward its background, and a light
        // one washes out faster. The symmetric #555555 gives 4.05:1 on #EEEEEE, below AA, where
        // #444444 gives 4.95:1.
        var muted = ink == Colors.White ? Color.FromRgb(0xAA, 0xAA, 0xAA) : Color.FromRgb(0x44, 0x44, 0x44);

        return new PopupPalette
        {
            Background = background,
            // No floor: full ink is by definition the most readable thing available.
            Title = Color.FromArgb(0xFF, ink.R, ink.G, ink.B),
            Description = Level(background, ink, muted, 0xCC, BodyFloor),
            GameLine = Level(background, ink, muted, 0x99, GameLineFloor),
            Footer = Level(background, ink, ink, 0xBB, BodyFloor),
            IconRing = Contrast(IconGold, background) >= NonTextFloor ? IconGold : Color.FromArgb(0xFF, ink.R, ink.G, ink.B)
        };
    }

    /// <summary>
    /// One text level: a base colour at an alpha, raised toward full ink only as far as the floor
    /// requires. The alpha is kept on the returned colour rather than folded away, because the popup's
    /// own fill is translucent and the text is meant to blend with what shows through it — the
    /// blending is only *judged* against the opaque fill.
    /// </summary>
    private static Color Level(Color background, Color ink, Color baseColour, byte alpha, double floor)
    {
        if (Contrast(Over(background, baseColour, alpha), background) >= floor)
            return Color.FromArgb(alpha, baseColour.R, baseColour.G, baseColour.B);

        // Even full ink can miss the floor near the crossover, where nothing does better than 4.58:1.
        if (Contrast(ink, background) <= floor)
            return Color.FromArgb(0xFF, ink.R, ink.G, ink.B);

        // Contrast rises monotonically as the level moves toward full ink, so the smallest sufficient
        // blend bisects. One parameter, and the result is still a colour with an alpha.
        var low = 0.0;
        var high = 1.0;
        for (var i = 0; i < 24; i++)
        {
            var mid = (low + high) / 2;
            var (colour, blended) = Toward(ink, baseColour, alpha, mid);
            if (Contrast(Over(background, colour, blended), background) >= floor)
                high = mid;
            else
                low = mid;
        }

        var (finalColour, finalAlpha) = Toward(ink, baseColour, alpha, high);
        return Color.FromArgb(finalAlpha, finalColour.R, finalColour.G, finalColour.B);
    }

    /// <summary>The level a fraction <paramref name="t"/> of the way from its base to full ink.</summary>
    private static (Color Colour, byte Alpha) Toward(Color ink, Color baseColour, byte alpha, double t) =>
        (Color.FromRgb(Lerp(baseColour.R, ink.R, t), Lerp(baseColour.G, ink.G, t), Lerp(baseColour.B, ink.B, t)),
         Lerp(alpha, 0xFF, t));

    private static byte Lerp(byte from, byte to, double t) => (byte)Math.Round(from + (to - from) * t);

    /// <summary>
    /// A colour at an alpha composited over an opaque reading of the background. The background's own
    /// alpha is ignored: behind it is a game frame, and no palette can reason about that.
    /// </summary>
    private static Color Over(Color background, Color colour, byte alpha)
    {
        var a = alpha / 255.0;
        return Color.FromRgb(Mix(colour.R, background.R, a), Mix(colour.G, background.G, a), Mix(colour.B, background.B, a));
    }

    private static byte Mix(byte front, byte back, double a) => (byte)Math.Round(front * a + back * (1 - a));

    /// <summary>WCAG relative luminance of an sRGB colour, alpha ignored.</summary>
    private static double RelativeLuminance(Color colour) =>
        0.2126 * Linear(colour.R) + 0.7152 * Linear(colour.G) + 0.0722 * Linear(colour.B);

    private static double Linear(byte value)
    {
        var channel = value / 255.0;
        return channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    private static double Contrast(Color first, Color second)
    {
        var a = RelativeLuminance(first);
        var b = RelativeLuminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}
