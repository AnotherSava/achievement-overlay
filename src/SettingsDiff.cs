namespace AchievementOverlay;

/// <summary>
/// Compares the settings a dialog collected against the ones currently in config. The result drives
/// two things: only changed keys are written back, and the host reads it to decide which live
/// re-wiring a save actually needs (a games rescan, a watcher rebuild, a hotkey re-registration).
/// </summary>
internal static class SettingsDiff
{
    /// <summary>
    /// Returns the user-facing settings that differ, keyed by <see cref="SettingsData"/> property
    /// name. App-managed state — the trackingConfigured map — is never part of the comparison, so a
    /// save can't drop or resurrect a game's setup confirmation.
    /// </summary>
    public static Dictionary<string, object?> Compute(SettingsData current, SettingsData edited)
    {
        var changed = new Dictionary<string, object?>();
        AddIfChanged(changed, nameof(SettingsData.GamesPaths), current.GamesPaths ?? "", edited.GamesPaths ?? "");
        AddIfChanged(changed, nameof(SettingsData.GseSavesPaths), current.GseSavesPaths, edited.GseSavesPaths);
        AddIfChanged(changed, nameof(SettingsData.Language), current.Language, edited.Language);
        AddIfChanged(changed, nameof(SettingsData.Font), current.Font, edited.Font);
        AddIfChanged(changed, nameof(SettingsData.Scale), current.Scale, edited.Scale);
        AddIfChanged(changed, nameof(SettingsData.NotificationPosition), current.NotificationPosition, edited.NotificationPosition);
        AddIfChanged(changed, nameof(SettingsData.NotificationBackground), current.NotificationBackground, edited.NotificationBackground);
        AddIfChanged(changed, nameof(SettingsData.SoundEnabled), current.SoundEnabled, edited.SoundEnabled);
        AddIfChanged(changed, nameof(SettingsData.SoundPath), current.SoundPath, edited.SoundPath);
        AddIfChanged(changed, nameof(SettingsData.DisplayDuration), current.DisplayDuration, edited.DisplayDuration);
        AddIfChanged(changed, nameof(SettingsData.UseGameOverlaySettings), current.UseGameOverlaySettings, edited.UseGameOverlaySettings);
        AddIfChanged(changed, nameof(SettingsData.RecentAchievementsShortcut), current.RecentAchievementsShortcut, edited.RecentAchievementsShortcut);
        AddIfChanged(changed, nameof(SettingsData.RecentAchievementsCount), current.RecentAchievementsCount, edited.RecentAchievementsCount);
        // A key absent from config and a key the user cleared are the same state, so compare both as "".
        AddIfChanged(changed, nameof(SettingsData.SteamWebApiKey), current.SteamWebApiKey ?? "", edited.SteamWebApiKey ?? "");
        AddIfChanged(changed, nameof(SettingsData.FirecrawlApiKey), current.FirecrawlApiKey ?? "", edited.FirecrawlApiKey ?? "");
        return changed;
    }

    private static void AddIfChanged<T>(Dictionary<string, object?> changed, string name, T current, T edited)
    {
        if (!EqualityComparer<T>.Default.Equals(current, edited))
            changed[name] = edited;
    }
}
