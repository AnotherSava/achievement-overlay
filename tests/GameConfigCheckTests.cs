using System.IO;
using Xunit;

namespace AchievementOverlay.Tests;

public sealed class GameConfigCheckTests : IDisposable
{
    private readonly string _gameDir;
    private readonly string _settingsDir;
    private readonly string _savesDir;

    public GameConfigCheckTests()
    {
        _gameDir = Path.Combine(Path.GetTempPath(), "GameConfigCheckTests_" + Guid.NewGuid().ToString("N"));
        _settingsDir = Path.Combine(_gameDir, "steam_settings");
        _savesDir = Path.Combine(_gameDir, "saves");
        Directory.CreateDirectory(Path.Combine(_settingsDir, "achievement_images"));
        Directory.CreateDirectory(_savesDir);
        File.WriteAllText(Path.Combine(_gameDir, "steam_api64.dll"), "");
        File.WriteAllText(Path.Combine(_settingsDir, "achievement_images", "A.jpg"), "");
    }

    public void Dispose()
    {
        if (Directory.Exists(_gameDir))
            Directory.Delete(_gameDir, true);
    }

    private GameInfo Game(string description = "Do the thing", string icon = "achievement_images/A.jpg")
    {
        File.WriteAllText(Path.Combine(_settingsDir, "achievements.json"), $$"""[{"name": "A", "displayName": "A", "description": "{{description}}", "icon": "{{icon}}"}]""");
        return new GameInfo { AppId = "480", GameName = "Game", MetadataPath = Path.Combine(_settingsDir, "achievements.json"), SettingsDirs = [_settingsDir] };
    }

    /// <summary>The checks for a RUNE game, with its save folder watched or with only an unrelated one.</summary>
    private List<string> Check(GameInfo game, string emulator = "RUNE", bool watched = true) =>
        GameList.ConfigProblemsOf(game, emulator, watched ? [AppConfig.ExpandEnvironmentVariables(AchievementOverlay.GbeConfig.SceneEmulatorDetector.RuneSaves)] : [_savesDir]);

    [Fact]
    public void Complete_HasNoProblems() => Assert.Empty(Check(Game()));

    [Theory]
    [InlineData("")]
    [InlineData("No description available")]
    public void MissingDescription_IsAProblem(string description) =>
        Assert.Contains(Check(Game(description)), problem => problem.Contains("without a description", StringComparison.Ordinal));

    [Fact]
    public void MissingIcon_IsAProblem() =>
        Assert.Contains(Check(Game(icon: "achievement_images/missing.jpg")), problem => problem.Contains("without an icon", StringComparison.Ordinal));

    [Fact]
    public void NoSteamLibraryBesideTheList_IsAProblem()
    {
        File.Delete(Path.Combine(_gameDir, "steam_api64.dll"));
        Assert.Contains(Check(Game()), problem => problem.Contains("No Steam library", StringComparison.Ordinal));
    }

    [Fact]
    public void EmulatorSavesNotWatched_IsAProblem() =>
        Assert.Contains(Check(Game(), watched: false), problem => problem.Contains("not one of the save folders", StringComparison.Ordinal));

    [Fact]
    public void SteamsOwnLibrary_IsAProblem() =>
        Assert.Contains(Check(Game(), emulator: "Steam"), problem => problem.Contains("Steam's own library", StringComparison.Ordinal));
}
