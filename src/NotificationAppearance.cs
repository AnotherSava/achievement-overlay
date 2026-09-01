using AchievementOverlay.GbeOverlay;

namespace AchievementOverlay;

/// <summary>
/// How a popup is drawn, resolved in one place. The unlock popup, the recent achievements panel and
/// the settings preview all build their windows from this, so the three cannot drift apart in font,
/// size or duration — the failure that a copy-pasted "read three values from config" invites.
/// </summary>
public sealed record NotificationAppearance
{
    /// <summary>The family used when none is configured, or the configured one can't be resolved.</summary>
    public const string DefaultFont = "Segoe UI";

    /// <summary>
    /// How far a game's own config may push the duration. Wider than the settings slider's 1–30,
    /// because a value written into an ini is deliberate, but not wide enough to pin a topmost popup
    /// on screen for minutes.
    /// </summary>
    internal const int MinGameDurationSeconds = 1;
    internal const int MaxGameDurationSeconds = 60;

    public required int DurationSeconds { get; init; }

    /// <summary>Font family name from config; see <see cref="ResolvedFont"/>.</summary>
    public required string Font { get; init; }

    public required NotificationScale Scale { get; init; }

    /// <summary>
    /// Which edge the popup is drawn against. App-owned with no per-game override: a position is not
    /// additive the way a sound or a font is, so a game's ini must not silently move a popup the user
    /// has just placed deliberately — and the recent panel, which is app-owned by construction, would
    /// then review an unlock in a different corner from the one it appeared in.
    /// </summary>
    public required NotificationAnchor Anchor { get; init; }

    /// <summary>
    /// The colour behind the popup's text. App-owned for the same reason as <see cref="Anchor"/>, and
    /// because the popup's look is the app's identity over a game rather than something an emulator
    /// config should restyle.
    /// </summary>
    public required PopupBackground Background { get; init; }

    /// <summary>A TrueType file the game supplies, which wins over <see cref="Font"/> when it loads.</summary>
    public string? FontFilePath { get; init; }

    public bool SoundEnabled { get; init; }

    /// <summary>Empty or null means the built-in sound.</summary>
    public string? SoundPath { get; init; }

    /// <summary>
    /// True when <see cref="SoundPath"/> came from the game rather than from the user, which is what
    /// decides whether a file that won't play falls back to the built-in sound or stays silent.
    /// </summary>
    public bool SoundIsFromGame { get; init; }

    /// <summary>The font family to draw with, falling back when the setting is blank.</summary>
    public string ResolvedFont => string.IsNullOrWhiteSpace(Font) ? DefaultFont : Font.Trim();

    /// <summary>
    /// The whole precedence, in one pure function: the app's settings are the baseline, and a game's
    /// own GBE config overrides them key by key. No IO, no WPF, no <see cref="AppConfig"/> — so every
    /// rule below is testable without a window.
    /// </summary>
    /// <param name="game">
    /// The game's own settings, or null when there are none, when the game isn't one this app can
    /// locate on disk, or when the user has turned the whole thing off.
    /// </param>
    public static NotificationAppearance Resolve(SettingsData app, GameOverlaySettings? game) => new()
    {
        DurationSeconds = game?.AchievementDurationSeconds is { } seconds
            ? Math.Clamp((int)Math.Round(seconds), MinGameDurationSeconds, MaxGameDurationSeconds)
            : app.DisplayDuration,
        Font = app.Font,
        Scale = app.Scale,
        Anchor = app.NotificationPosition,
        Background = app.NotificationBackground,
        FontFilePath = game?.FontFilePath,
        // The master switch stays app-owned: 'no sound' means no sound, whatever a game ships. Only
        // the file is overridable.
        SoundEnabled = app.SoundEnabled,
        SoundPath = app.SoundEnabled ? game?.SoundFilePath ?? app.SoundPath : null,
        SoundIsFromGame = app.SoundEnabled && game?.SoundFilePath != null
    };

    /// <summary>
    /// App settings only — the recent panel, the settings preview, and any popup with no single game
    /// behind it. The panel stacks entries from several games at once, so no one game's config can
    /// speak for the stack.
    /// </summary>
    public static NotificationAppearance From(SettingsData app) => Resolve(app, null);

    public static NotificationAppearance From(AppConfig config) => From(config.GetCurrent());
}
