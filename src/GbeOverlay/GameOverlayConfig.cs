using System.Globalization;

namespace AchievementOverlay.GbeOverlay;

/// <summary>
/// What a game's GBE config says about the two things this app can honour. Deliberately narrow: the
/// keys that were rejected <em>as per-game overrides</em> — position, colours, rounding, margins, font
/// and icon sizes — are not parsed "for later", because a value nothing reads is a value nothing keeps
/// correct. Position and background colour are app settings in their own right; a game's ini still
/// does not move or recolour a popup the user placed deliberately.
/// The reasoning is in docs/plans/completed/2026-08-18-per-game-overlay-settings.md and
/// docs/plans/2026-08-30-popup-position-and-background.md.
/// </summary>
public sealed record GameOverlayConfig(double? AchievementDurationSeconds, string? FontOverride)
{
    private const string AppearanceSection = "overlay::appearance";

    public static GameOverlayConfig Parse(IniFile ini) =>
        new(ParseSeconds(ini.Get(AppearanceSection, "Notification_Duration_Achievement")),
            Strip(ini.Get(AppearanceSection, "Font_Override")));

    /// <summary>
    /// GBE reads these through <c>std::stof</c>, which takes the leading numeric prefix and throws on
    /// anything else — a throw it catches per key, so one bad value costs that value alone. A
    /// duration of zero or less is <em>not</em> reproduced: over there it suppresses the notification,
    /// and an optional nicety that can silence one game is the worst bug report this feature could
    /// earn.
    /// </summary>
    private static double? ParseSeconds(string? value)
    {
        var seconds = ParseLeadingDouble(value);
        return seconds is > 0 ? seconds : null;
    }

    internal static double? ParseLeadingDouble(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim();
        var end = 0;
        if (end < text.Length && (text[end] == '+' || text[end] == '-'))
            end++;
        while (end < text.Length && char.IsAsciiDigit(text[end]))
            end++;
        if (end < text.Length && text[end] == '.')
        {
            end++;
            while (end < text.Length && char.IsAsciiDigit(text[end]))
                end++;
        }

        return double.TryParse(text[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static string? Strip(string? value)
    {
        var stripped = value?.Trim();
        return string.IsNullOrEmpty(stripped) ? null : stripped;
    }
}
