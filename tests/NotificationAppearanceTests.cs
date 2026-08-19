using AchievementOverlay.GbeOverlay;
using Xunit;

namespace AchievementOverlay.Tests;

public class NotificationAppearanceTests
{
    private static SettingsData App() => new()
    {
        GamesPaths = @"C:\Games",
        GseSavesPaths = @"%appdata%\GSE Saves",
        Language = "english",
        Font = "Segoe UI",
        Scale = NotificationScale.Default,
        SoundEnabled = true,
        SoundPath = "",
        DisplayDuration = 7,
        UseGameOverlaySettings = true,
        RecentAchievementsCount = 5
    };

    private static GameOverlaySettings Game(double? duration = null, string? sound = null, string? font = null) =>
        new(duration, sound, font, @"C:\Games\Example\steam_settings");

    [Fact]
    public void Resolve_NoGameSettings_UsesAppValues()
    {
        var appearance = NotificationAppearance.Resolve(App(), null);

        Assert.Equal(7, appearance.DurationSeconds);
        Assert.Equal("Segoe UI", appearance.Font);
        Assert.Null(appearance.FontFilePath);
        Assert.True(appearance.SoundEnabled);
        Assert.Equal("", appearance.SoundPath);
        Assert.False(appearance.SoundIsFromGame);
    }

    [Fact]
    public void Resolve_PartialGameSettings_LeavesTheOtherFieldsAtAppValues()
    {
        var appearance = NotificationAppearance.Resolve(App(), Game(font: @"C:\Games\Example\steam_settings\fonts\poppins.ttf"));

        Assert.Equal(7, appearance.DurationSeconds);
        Assert.Equal("", appearance.SoundPath);
        Assert.Equal(@"C:\Games\Example\steam_settings\fonts\poppins.ttf", appearance.FontFilePath);
    }

    [Fact]
    public void Resolve_GameDuration_OverridesTheAppDuration()
    {
        Assert.Equal(12, NotificationAppearance.Resolve(App(), Game(duration: 12)).DurationSeconds);
    }

    [Fact]
    public void Resolve_FractionalGameDuration_IsRounded()
    {
        Assert.Equal(8, NotificationAppearance.Resolve(App(), Game(duration: 7.6)).DurationSeconds);
    }

    [Theory]
    [InlineData(0.2, NotificationAppearance.MinGameDurationSeconds)]
    [InlineData(600, NotificationAppearance.MaxGameDurationSeconds)]
    public void Resolve_GameDurationOutOfRange_IsClamped(double seconds, int expected)
    {
        Assert.Equal(expected, NotificationAppearance.Resolve(App(), Game(duration: seconds)).DurationSeconds);
    }

    [Fact]
    public void Resolve_GameSound_OverridesTheAppSound()
    {
        var app = App();
        app.SoundPath = @"C:\my.wav";

        var appearance = NotificationAppearance.Resolve(app, Game(sound: @"C:\Games\Example\steam_settings\sounds\unlock.wav"));

        Assert.Equal(@"C:\Games\Example\steam_settings\sounds\unlock.wav", appearance.SoundPath);
        // A game's file that won't play falls back to the built-in sound; a user's does not.
        Assert.True(appearance.SoundIsFromGame);
    }

    [Fact]
    public void Resolve_UserSoundPath_IsNotMarkedAsComingFromTheGame()
    {
        var app = App();
        app.SoundPath = @"C:\my.wav";

        var appearance = NotificationAppearance.Resolve(app, Game(duration: 12));

        Assert.Equal(@"C:\my.wav", appearance.SoundPath);
        Assert.False(appearance.SoundIsFromGame);
    }

    [Fact]
    public void Resolve_SoundDisabled_StaysSilentWhateverTheGameShips()
    {
        var app = App();
        app.SoundEnabled = false;

        var appearance = NotificationAppearance.Resolve(app, Game(sound: @"C:\Games\Example\steam_settings\sounds\unlock.wav"));

        Assert.False(appearance.SoundEnabled);
        Assert.Null(appearance.SoundPath);
        Assert.False(appearance.SoundIsFromGame);
    }

    [Fact]
    public void From_IgnoresGameSettingsEntirely()
    {
        // What the recent panel and the settings preview use.
        var appearance = NotificationAppearance.From(App());

        Assert.Equal(7, appearance.DurationSeconds);
        Assert.Null(appearance.FontFilePath);
    }

    [Theory]
    [InlineData("", NotificationAppearance.DefaultFont)]
    [InlineData("   ", NotificationAppearance.DefaultFont)]
    [InlineData("  Verdana ", "Verdana")]
    public void ResolvedFont_FallsBackWhenBlank(string configured, string expected)
    {
        var app = App();
        app.Font = configured;

        Assert.Equal(expected, NotificationAppearance.From(app).ResolvedFont);
    }
}
