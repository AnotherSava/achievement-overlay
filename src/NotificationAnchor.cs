using System.Text.Json;
using System.Text.Json.Serialization;

namespace AchievementOverlay;

/// <summary>
/// Which corner or edge of the display popups appear at. The names are GBE's own — its
/// <c>PosAchievement</c> takes exactly these six spellings — so a value here reads the same way as one
/// in a <c>configs.overlay.ini</c> the user has already edited. A game's own key is still not read;
/// the reasoning is in docs/plans/2026-08-30-popup-position-and-background.md.
/// </summary>
/// <remarks>
/// The converter is attached to the <em>type</em> for the same reason
/// <see cref="NotificationScale"/>'s is: <see cref="AppConfig.UpdateConfigValues"/> boxes each changed
/// value into an object dictionary and serializes it on its own, where a property-scoped converter is
/// skipped — and a bare enum then writes as the integer 3, which no one reading config.json can act on.
/// </remarks>
[JsonConverter(typeof(NotificationAnchorConverter))]
public enum NotificationAnchor
{
    /// <summary>
    /// Where the popup has always gone, and GBE's own default for an earned achievement. It has to be
    /// member 0: no existing config carries the position key, so an absent one deserialises to
    /// <c>default(NotificationAnchor)</c>, which must leave every install exactly as it was.
    /// </summary>
    BottomRight = 0,
    BottomCenter,
    BottomLeft,
    TopRight,
    TopCenter,
    TopLeft
}

/// <summary>Reading and writing the config form of a <see cref="NotificationAnchor"/>.</summary>
public static class NotificationAnchors
{
    /// <summary>
    /// Reads GBE's spelling, tolerantly: case, separators, and <c>bot</c> against <c>bottom</c> are all
    /// ignored, so <c>top_right</c>, <c>Top-Right</c> and <c>TOPRIGHT</c> are one value. Anything
    /// unrecognised reads as <see cref="NotificationAnchor.BottomRight"/> rather than throwing — a
    /// hand-edited value must cost at most the popup's position, never the app's startup.
    /// </summary>
    public static NotificationAnchor Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return NotificationAnchor.BottomRight;

        var name = new string(text.Where(c => c is not ('-' or '_' or ' ')).ToArray()).ToLowerInvariant();
        if (name.StartsWith("bottom"))
            name = "bot" + name["bottom".Length..];

        return name switch
        {
            "topleft" => NotificationAnchor.TopLeft,
            "topcenter" => NotificationAnchor.TopCenter,
            "topright" => NotificationAnchor.TopRight,
            "botleft" => NotificationAnchor.BottomLeft,
            "botcenter" => NotificationAnchor.BottomCenter,
            _ => NotificationAnchor.BottomRight
        };
    }

    /// <summary>The form written to config — GBE's spelling, so the two files say it the same way.</summary>
    public static string ToConfigString(this NotificationAnchor anchor) => anchor switch
    {
        NotificationAnchor.TopLeft => "top_left",
        NotificationAnchor.TopCenter => "top_center",
        NotificationAnchor.TopRight => "top_right",
        NotificationAnchor.BottomLeft => "bot_left",
        NotificationAnchor.BottomCenter => "bot_center",
        _ => "bot_right"
    };
}

/// <summary>
/// Reads the anchor from GBE's spelling and writes it back the same way. Deliberately unlike
/// <see cref="NotificationScaleConverter"/>, it never throws: a <c>JsonException</c> out of here would
/// escape <see cref="AppConfig"/>'s load and put the config-error dialog in front of a user whose only
/// mistake was mistyping a corner.
/// </summary>
internal sealed class NotificationAnchorConverter : JsonConverter<NotificationAnchor>
{
    public override NotificationAnchor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return NotificationAnchors.Parse(reader.GetString());

        reader.Skip(); // steps over a whole object or array; a stray scalar needs no skipping
        return NotificationAnchor.BottomRight;
    }

    public override void Write(Utf8JsonWriter writer, NotificationAnchor value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToConfigString());
}
