using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace AchievementOverlay;

/// <summary>
/// The colour behind the popup's text, alpha included. One self-describing <c>#AARRGGBB</c> string
/// rather than a colour key plus an opacity key: the two would need reconciling on every read, and the
/// shipped value is 87% opaque, so the alpha is part of the answer rather than a modifier of it.
/// </summary>
/// <remarks>
/// The converter is attached to the <em>type</em> for the reason
/// <see cref="NotificationScale"/>'s doc comment sets out, and it never throws. A plain string property
/// would not have avoided that: System.Text.Json refuses a number or an object into a string too, and
/// the resulting exception escapes <see cref="AppConfig"/>'s load into the config-error dialog.
/// </remarks>
[JsonConverter(typeof(PopupBackgroundConverter))]
public readonly record struct PopupBackground
{
    /// <summary>
    /// Alpha floor. Below this the popup is a smear over the game, which arrives as "notifications
    /// stopped working" rather than as "I picked a bad colour" — so a hand-edited <c>#001A1A2E</c> is
    /// clamped on the way in, the way <see cref="NotificationScale.ScreenPercent"/> clamps its range.
    /// </summary>
    public const byte MinAlpha = 0x66;

    /// <summary>The look the app has always had, and what any unreadable value falls back to.</summary>
    public static PopupBackground Default { get; } = From(0xDD, 0x1A, 0x1A, 0x2E);

    public byte A { get; private init; }
    public byte R { get; private init; }
    public byte G { get; private init; }
    public byte B { get; private init; }

    public static PopupBackground From(byte a, byte r, byte g, byte b) =>
        new() { A = Math.Clamp(a, MinAlpha, byte.MaxValue), R = r, G = g, B = b };

    /// <summary>The same colour at a different opacity — what the settings slider produces.</summary>
    public PopupBackground WithAlpha(byte alpha) => From(alpha, R, G, B);

    /// <summary>The same opacity in a different colour — what a swatch or the picker produces.</summary>
    public PopupBackground WithColour(Color colour) => From(A, colour.R, colour.G, colour.B);

    public Color ToColor() => Color.FromArgb(A, R, G, B);

    /// <summary>True when this is the same colour as <paramref name="colour"/>, opacity aside.</summary>
    public bool IsColour(Color colour) => R == colour.R && G == colour.G && B == colour.B;

    /// <summary>
    /// Reads <c>#AARRGGBB</c>, <c>#RRGGBB</c>, <c>#ARGB</c> or <c>#RGB</c>, with the <c>#</c> optional
    /// and the short forms expanded by digit doubling. The three- and six-digit forms have no alpha of
    /// their own and take the default's.
    /// </summary>
    /// <remarks>
    /// Hand-written rather than WPF's <c>ColorConverter</c>, which has no TryParse, returns null for
    /// null, throws <c>FormatException</c> for junk and <c>InvalidOperationException</c> on its
    /// <c>sc#</c> branch — three different failures to catch for a value whose only job is to be
    /// readable.
    /// </remarks>
    public static PopupBackground Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Default;

        var hex = text.Trim();
        if (hex.StartsWith('#'))
            hex = hex[1..];

        if (!hex.All(char.IsAsciiHexDigit))
            return Default;

        return hex.Length switch
        {
            3 => From(Default.A, Doubled(hex, 0), Doubled(hex, 1), Doubled(hex, 2)),
            4 => From(Doubled(hex, 0), Doubled(hex, 1), Doubled(hex, 2), Doubled(hex, 3)),
            6 => From(Default.A, Pair(hex, 0), Pair(hex, 2), Pair(hex, 4)),
            8 => From(Pair(hex, 0), Pair(hex, 2), Pair(hex, 4), Pair(hex, 6)),
            _ => Default
        };
    }

    private static byte Pair(string hex, int index) => Convert.ToByte(hex.Substring(index, 2), 16);

    private static byte Doubled(string hex, int index) => Convert.ToByte(new string(hex[index], 2), 16);

    /// <summary>Always eight digits, so the app's own writes round-trip exactly.</summary>
    public override string ToString() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// Reads the background colour from its string form and writes it back the same way. Every unexpected
/// token reads as the default rather than throwing: the alternative is a config-error dialog instead
/// of an app, for a mistyped colour.
/// </summary>
internal sealed class PopupBackgroundConverter : JsonConverter<PopupBackground>
{
    public override PopupBackground Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return PopupBackground.Parse(reader.GetString());

        reader.Skip(); // steps over a whole object or array; a stray scalar needs no skipping
        return PopupBackground.Default;
    }

    public override void Write(Utf8JsonWriter writer, PopupBackground value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
