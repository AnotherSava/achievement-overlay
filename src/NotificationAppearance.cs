namespace AchievementOverlay;

/// <summary>
/// How a popup is drawn, resolved from config in one place. The unlock popup and the recent
/// achievements panel both build their windows from this, so the two cannot drift apart in font,
/// size or duration — the failure that a copy-pasted "read three values from config" invites.
/// </summary>
public sealed record NotificationAppearance(int DurationSeconds, string Font, NotificationScale Scale)
{
    /// <summary>The family used when none is configured, or the configured one can't be resolved.</summary>
    public const string DefaultFont = "Segoe UI";

    public static NotificationAppearance From(AppConfig config) =>
        new(config.DisplayDuration, config.Font, config.Scale);

    /// <summary>The font family to draw with, falling back when the setting is blank.</summary>
    public string ResolvedFont => string.IsNullOrWhiteSpace(Font) ? DefaultFont : Font.Trim();
}
