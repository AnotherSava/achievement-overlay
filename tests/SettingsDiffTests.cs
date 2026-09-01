using AchievementOverlay;
using Xunit;

namespace AchievementOverlay.Tests;

public class SettingsDiffTests
{
    private static SettingsData Sample() => new()
    {
        GamesPaths = @"C:\Games",
        GseSavesPaths = @"%appdata%\GSE Saves",
        Language = "english",
        SoundEnabled = true,
        SoundPath = "",
        DisplayDuration = 7,
        UseGameOverlaySettings = true,
        RecentAchievementsShortcut = "Ctrl+Shift+H",
        RecentAchievementsCount = 5,
        SteamWebApiKey = "abc",
        FirecrawlApiKey = null
    };

    [Fact]
    public void Compute_IdenticalSettings_ReturnsNothing()
    {
        Assert.Empty(SettingsDiff.Compute(Sample(), Sample()));
    }

    [Fact]
    public void Compute_ReturnsOnlyChangedKeys()
    {
        var edited = Sample();
        edited.DisplayDuration = 10;
        edited.SoundEnabled = false;

        var changed = SettingsDiff.Compute(Sample(), edited);

        Assert.Equal(2, changed.Count);
        Assert.Equal(10, changed[nameof(SettingsData.DisplayDuration)]);
        Assert.Equal(false, changed[nameof(SettingsData.SoundEnabled)]);
    }

    [Fact]
    public void Compute_NotificationPositionChanged_IsReported()
    {
        // Without this line the user picks a corner, clicks Save, and nothing is written and nothing
        // is reported — the failure a new setting invites most.
        var edited = Sample();
        edited.NotificationPosition = NotificationAnchor.TopRight;

        var changed = SettingsDiff.Compute(Sample(), edited);

        Assert.Equal(NotificationAnchor.TopRight, changed[nameof(SettingsData.NotificationPosition)]);
    }

    [Fact]
    public void Compute_NotificationBackgroundChanged_IsReported()
    {
        var edited = Sample();
        edited.NotificationBackground = PopupBackground.Parse("#FF102030");

        var changed = SettingsDiff.Compute(Sample(), edited);

        Assert.Equal(PopupBackground.Parse("#FF102030"), changed[nameof(SettingsData.NotificationBackground)]);
    }

    [Fact]
    public void Compute_UseGameOverlaySettingsChanged_IsReported()
    {
        var edited = Sample();
        edited.UseGameOverlaySettings = false;

        var changed = SettingsDiff.Compute(Sample(), edited);

        Assert.Equal(false, changed[nameof(SettingsData.UseGameOverlaySettings)]);
    }

    [Fact]
    public void Compute_KeysAreSettingsDataPropertyNames()
    {
        // The host keys its live re-wiring off these names, so they have to stay property names
        // rather than the camelCase they're written under.
        var edited = Sample();
        edited.GamesPaths = @"C:\Games;D:\More";
        edited.GseSavesPaths = @"D:\Saves";
        edited.RecentAchievementsShortcut = "Ctrl+Alt+R";

        var changed = SettingsDiff.Compute(Sample(), edited);

        Assert.Contains(nameof(SettingsData.GamesPaths), changed.Keys);
        Assert.Contains(nameof(SettingsData.GseSavesPaths), changed.Keys);
        Assert.Contains(nameof(SettingsData.RecentAchievementsShortcut), changed.Keys);
    }

    [Fact]
    public void Compute_NullAndEmptyApiKey_AreTheSameState()
    {
        // An absent key and a cleared field mean the same thing, so switching between them must not
        // register as a change (and rewrite config on every OK).
        var current = Sample();
        current.FirecrawlApiKey = null;
        var edited = Sample();
        edited.FirecrawlApiKey = "";

        Assert.Empty(SettingsDiff.Compute(current, edited));
    }

    [Fact]
    public void Compute_ClearedApiKey_IsWrittenAsEmpty()
    {
        var edited = Sample();
        edited.SteamWebApiKey = "";

        var changed = SettingsDiff.Compute(Sample(), edited);

        Assert.Equal("", changed[nameof(SettingsData.SteamWebApiKey)]);
    }

    [Fact]
    public void Compute_IgnoresTrackingConfigured()
    {
        // App-managed state: a save must not be able to drop or resurrect a setup confirmation.
        var current = Sample();
        current.TrackingConfigured = new Dictionary<string, long> { ["1601580"] = 1 };
        var edited = Sample();
        edited.TrackingConfigured = null;

        Assert.Empty(SettingsDiff.Compute(current, edited));
    }
}
