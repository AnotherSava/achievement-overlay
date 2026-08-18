using System.Text.Json;
using System.Text.Json.Serialization;

namespace AchievementOverlay;

/// <summary>The unit a <see cref="NotificationScale"/> is expressed in.</summary>
public enum ScaleUnit
{
    /// <summary>A share of the display's logical width, so the popup keeps its apparent size on any monitor.</summary>
    ScreenPercent,

    /// <summary>An absolute width in device-independent pixels, the same on every monitor.</summary>
    Pixels
}

/// <summary>
/// How wide the popup is drawn. Everything inside is laid out at a fixed design size and multiplied
/// by one transform, so setting the width scales the icon, padding, wrap width and text together.
/// Deliberately one setting rather than a size plus a separate text size: the two overlap, and a
/// large popup with small text is a combination neither value alone would explain.
/// There is no separate "automatic" mode, because automatic was only ever 15% of the display's
/// width — which this expresses directly, in a number that means something on screen.
/// </summary>
/// <remarks>
/// The converter is attached to the <em>type</em>, not to the property on <see cref="SettingsData"/>.
/// A property-scoped converter is skipped whenever the value is serialized on its own — which is
/// exactly what <see cref="AppConfig.UpdateConfigValues"/> does, boxing it into an object dictionary
/// — and the struct then writes as {"Unit":0,"Value":15}, which cannot be read back.
/// </remarks>
[JsonConverter(typeof(NotificationScaleConverter))]
public readonly record struct NotificationScale
{
    /// <summary>The width the popup is laid out at before scaling: padding 24 + icon 56 + gap 12 + text 230.</summary>
    public const double DesignWidth = 322;

    /// <summary>Guards both units: a popup below or above these is unreadable or absurd.</summary>
    public const double MinFactor = 0.75;
    public const double MaxFactor = 2.5;

    /// <summary>What automatic always meant, and what a fresh config gets.</summary>
    public const double DefaultScreenPercent = 15;

    public ScaleUnit Unit { get; private init; }

    /// <summary>Percent of the display's width, or a pixel width, depending on <see cref="Unit"/>.</summary>
    public double Value { get; private init; }

    public static NotificationScale Default => ScreenPercent(DefaultScreenPercent);

    public static NotificationScale ScreenPercent(double percent) =>
        new() { Unit = ScaleUnit.ScreenPercent, Value = Math.Clamp(percent, 1, 100) };

    public static NotificationScale Pixels(double pixels) =>
        new() { Unit = ScaleUnit.Pixels, Value = Math.Clamp(pixels, 100, 2000) };

    /// <summary>
    /// The popup's width on a display of the given logical width, before the readability clamp.
    /// </summary>
    public double WidthOn(double displayLogicalWidth) =>
        Unit == ScaleUnit.ScreenPercent ? displayLogicalWidth * Value / 100.0 : Value;

    /// <summary>
    /// Reads "15%", "384px" or a bare number (pixels). Anything unrecognised reads as the default
    /// rather than throwing: a hand-edited value must cost at most the popup's size, never the
    /// app's startup.
    /// </summary>
    public static NotificationScale Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Default;

        var trimmed = text.Trim();
        var isPercent = trimmed.EndsWith('%');
        var number = trimmed.TrimEnd('%').TrimEnd('x', 'X', 'p', 'P').Trim();
        if (!double.TryParse(number, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
            return Default;

        return isPercent ? ScreenPercent(value) : Pixels(value);
    }

    /// <summary>The form written to config — self-describing, so the unit survives the round trip.</summary>
    public override string ToString() =>
        Unit == ScaleUnit.ScreenPercent
            ? $"{Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}%"
            : $"{Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}px";
}

/// <summary>
/// Reads the scale from a string ("15%", "384px") or a bare number (pixels), and writes it back in
/// the self-describing string form. Property-scoped, so the shared JsonOptions is untouched.
/// </summary>
internal sealed class NotificationScaleConverter : JsonConverter<NotificationScale>
{
    public override NotificationScale Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return NotificationScale.Default;
            case JsonTokenType.Number:
                return NotificationScale.Pixels(reader.GetDouble());
            case JsonTokenType.String:
                return NotificationScale.Parse(reader.GetString());
            default:
                throw new JsonException($"Cannot convert token {reader.TokenType} to a scale.");
        }
    }

    public override void Write(Utf8JsonWriter writer, NotificationScale value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
