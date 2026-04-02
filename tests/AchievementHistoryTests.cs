using System.IO;
using System.Text.Json;
using AchievementOverlay;
using Xunit;

namespace AchievementOverlay.Tests;

public class AchievementHistoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _gseSavesDir;
    private readonly string _gamesDir;

    public AchievementHistoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AchHistoryTests_" + Guid.NewGuid().ToString("N"));
        _gseSavesDir = Path.Combine(_tempDir, "GSE Saves");
        _gamesDir = Path.Combine(_tempDir, "Games");
        Directory.CreateDirectory(_gseSavesDir);
        Directory.CreateDirectory(_gamesDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private AppConfig CreateConfig()
    {
        var configPath = Path.Combine(_tempDir, "config.json");
        var config = new
        {
            gseSavesPath = _gseSavesDir,
            gamesPaths = _gamesDir,
            language = "english",
            soundEnabled = true,
            soundPath = "",
            displayDuration = 7,
            recentAchievementsShortcut = "Ctrl+Shift+H",
            recentAchievementsCount = 5
        };
        File.WriteAllText(configPath, JsonSerializer.Serialize(config));
        return new AppConfig(configPath);
    }

    private void CreateGame(string appId, string gameName)
    {
        var gameDir = Path.Combine(_gamesDir, gameName);
        Directory.CreateDirectory(gameDir);
        File.WriteAllText(Path.Combine(gameDir, "steam_appid.txt"), appId);
        var ssDir = Path.Combine(gameDir, "steam_settings");
        Directory.CreateDirectory(ssDir);
        File.WriteAllText(Path.Combine(ssDir, "achievements.json"), """[{"name": "ACH01", "displayName": "Test Achievement", "description": "Test Desc"}]""");
    }

    private void CreateSaveFile(string appId, string json)
    {
        var dir = Path.Combine(_gseSavesDir, appId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "achievements.json"), json);
    }

    [Fact]
    public void GetRecent_NoGames_ReturnsSyntheticOnly()
    {
        var config = CreateConfig();
        var cache = new GameCache(new[] { _gamesDir });
        cache.ScanAll();
        var history = new AchievementHistory(config, cache);

        var recent = history.GetRecent(5);

        Assert.Single(recent);
        Assert.Equal("Achievement Connoisseur", recent[0].AchievementName);
        Assert.Equal("Achievement Overlay", recent[0].GameName);
    }

    [Fact]
    public void GetRecent_WithEarnedAchievements_ReturnsSortedByTime()
    {
        CreateGame("12345", "TestGame");
        CreateSaveFile("12345", """{"ACH01": {"earned": true, "earned_time": 1000}}""");

        var config = CreateConfig();
        var cache = new GameCache(new[] { _gamesDir });
        cache.ScanAll();
        var history = new AchievementHistory(config, cache);

        var recent = history.GetRecent(5);

        // Should have the real achievement + synthetic
        Assert.True(recent.Count >= 2);
        // Most recent first (synthetic will have a recent timestamp)
        Assert.Equal("Achievement Connoisseur", recent[0].AchievementName);
    }

    [Fact]
    public void GetRecent_OnlyTrackedGames_IgnoresUnknownAppIds()
    {
        // Create save file for appid with no matching game
        CreateSaveFile("99999", """{"ACH01": {"earned": true, "earned_time": 1000}}""");

        var config = CreateConfig();
        var cache = new GameCache(new[] { _gamesDir });
        cache.ScanAll();
        var history = new AchievementHistory(config, cache);

        var recent = history.GetRecent(5);

        // Only synthetic entry — 99999 is not tracked
        Assert.Single(recent);
        Assert.Equal("Achievement Connoisseur", recent[0].AchievementName);
    }

    [Fact]
    public void GetRecent_RespectsCountLimit()
    {
        CreateGame("12345", "TestGame");
        CreateSaveFile("12345", """{"ACH01": {"earned": true, "earned_time": 2000}}""");

        var config = CreateConfig();
        var cache = new GameCache(new[] { _gamesDir });
        cache.ScanAll();
        var history = new AchievementHistory(config, cache);

        var recent = history.GetRecent(1);

        Assert.Single(recent);
    }

    [Fact]
    public void GetRecent_SkipsUnearnedAchievements()
    {
        CreateGame("12345", "TestGame");
        CreateSaveFile("12345", """{"ACH01": {"earned": false, "earned_time": 0}}""");

        var config = CreateConfig();
        var cache = new GameCache(new[] { _gamesDir });
        cache.ScanAll();
        var history = new AchievementHistory(config, cache);

        var recent = history.GetRecent(5);

        // Only synthetic
        Assert.Single(recent);
    }

    [Fact]
    public void GetRecent_ResolvesDisplayName()
    {
        CreateGame("12345", "TestGame");
        CreateSaveFile("12345", """{"ACH01": {"earned": true, "earned_time": 1000}}""");

        var config = CreateConfig();
        var cache = new GameCache(new[] { _gamesDir });
        cache.ScanAll();
        var history = new AchievementHistory(config, cache);

        var recent = history.GetRecent(5);
        var gameAch = recent.FirstOrDefault(e => e.AppId == "12345");

        Assert.NotNull(gameAch);
        Assert.Equal("Test Achievement", gameAch!.AchievementName);
        Assert.Equal("TestGame", gameAch.GameName);
    }

    [Fact]
    public void GetRecent_SyntheticEntry_HasDescription()
    {
        var config = CreateConfig();
        var cache = new GameCache(new[] { _gamesDir });
        cache.ScanAll();
        var history = new AchievementHistory(config, cache);

        var recent = history.GetRecent(5);
        var synthetic = recent.First(e => e.AchievementName == "Achievement Connoisseur");

        Assert.Equal("Install and configure Achievement Overlay", synthetic.Description);
    }
}
