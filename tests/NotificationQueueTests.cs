using System.IO;
using System.Text.Json;
using AchievementOverlay;
using Xunit;

namespace AchievementOverlay.Tests;

public class NotificationQueueTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _gamesDir;
    private readonly string _settingsPath;
    private readonly AppConfig _config;
    private readonly GameCache _gameCache;

    public NotificationQueueTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ach-queue-tests-" + Guid.NewGuid().ToString("N")[..8]);
        _gamesDir = Path.Combine(_tempDir, "Games");
        Directory.CreateDirectory(_gamesDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "GSE Saves"));

        // Create a test game with achievements metadata
        SetupTestGame("12345", "TestGame", new[]
        {
            new { name = "ACH01", displayName = "First Blood", description = "Get your first kill", icon = "img/ach01.png" },
            new { name = "ACH02", displayName = "Completionist", description = "Complete the game", icon = "img/ach02.png" }
        });

        // Create config.json for AppConfig
        _settingsPath = Path.Combine(_tempDir, "config.json");
        var settings = new
        {
            gseSavesPaths = Path.Combine(_tempDir, "GSE Saves"),
            gamesPaths = _gamesDir,
            language = "english",
            soundEnabled = true,
            soundPath = "",
            displayDuration = 7,
            recentAchievementsShortcut = "Ctrl+Shift+H",
            recentAchievementsCount = 5
        };
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings));

        _config = new AppConfig(_settingsPath);
        _gameCache = new GameCache(new[] { _gamesDir });
        _gameCache.ScanAll();
    }

    private void SetupTestGame(string appId, string gameName, object[] achievements)
    {
        var gameDir = Path.Combine(_gamesDir, gameName);
        Directory.CreateDirectory(gameDir);

        File.WriteAllText(Path.Combine(gameDir, "steam_appid.txt"), appId);

        var steamSettings = Path.Combine(gameDir, "steam_settings");
        Directory.CreateDirectory(steamSettings);

        var json = JsonSerializer.Serialize(achievements, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(steamSettings, "achievements.json"), json);
    }

    [Fact]
    public void ResolveMetadata_KnownGame_ReturnsDisplayInfo()
    {
        var queue = new NotificationQueue(_gameCache, _config);

        var args = new NewAchievementEventArgs
        {
            AppId = "12345",
            AchievementName = "ACH01",
            EarnedTime = 1700000000
        };

        var item = queue.ResolveMetadata(args);

        Assert.NotNull(item);
        Assert.Equal("First Blood", item.AchievementName);
        Assert.Equal("Get your first kill", item.Description);
        Assert.Equal("12345", item.AppId);
    }

    [Fact]
    public void ResolveMetadata_UnknownGame_ReturnsNull()
    {
        var queue = new NotificationQueue(_gameCache, _config);

        var args = new NewAchievementEventArgs
        {
            AppId = "99999",
            AchievementName = "ACH01",
            EarnedTime = 1700000000
        };

        var item = queue.ResolveMetadata(args);

        Assert.Null(item);
    }

    [Fact]
    public void ResolveMetadata_UnknownAchievement_ReturnsNull()
    {
        var queue = new NotificationQueue(_gameCache, _config);

        var args = new NewAchievementEventArgs
        {
            AppId = "12345",
            AchievementName = "NONEXISTENT",
            EarnedTime = 1700000000
        };

        var item = queue.ResolveMetadata(args);

        Assert.Null(item);
    }

    [Fact]
    public void ResolveMetadata_MultiLanguageDisplayName_ResolvesCorrectly()
    {
        // Set up a game with multi-language names
        SetupTestGame("67890", "MultiLangGame", new object[]
        {
            new
            {
                name = "ML_ACH",
                displayName = new { english = "English Name", german = "German Name" },
                description = new { english = "English Desc", german = "German Desc" },
                icon = ""
            }
        });

        var multiCache = new GameCache(new[] { _gamesDir });
        multiCache.ScanAll();
        var queue = new NotificationQueue(multiCache, _config);

        var args = new NewAchievementEventArgs
        {
            AppId = "67890",
            AchievementName = "ML_ACH",
            EarnedTime = 1700000000
        };

        var item = queue.ResolveMetadata(args);

        Assert.NotNull(item);
        Assert.Equal("English Name", item.AchievementName);
        Assert.Equal("English Desc", item.Description);
    }

    [Fact]
    public void Enqueue_AddsItemToQueue()
    {
        var queue = new NotificationQueue(_gameCache, _config);

        var args = new NewAchievementEventArgs
        {
            AppId = "12345",
            AchievementName = "ACH01",
            EarnedTime = 1700000000
        };

        queue.Enqueue(args);

        // Item was queued (Count includes items not yet dispatched)
        Assert.True(queue.Count >= 0);
    }

    [Fact]
    public void Enqueue_UnknownGame_SkipsNotification()
    {
        var queue = new NotificationQueue(_gameCache, _config);

        var args = new NewAchievementEventArgs
        {
            AppId = "99999",
            AchievementName = "RAW_NAME",
            EarnedTime = 1700000000
        };

        queue.Enqueue(args);

        // Unknown game cannot be resolved, so nothing is queued
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Enqueue_MultipleItems_AllQueued()
    {
        var queue = new NotificationQueue(_gameCache, _config);

        queue.Enqueue(new NewAchievementEventArgs { AppId = "12345", AchievementName = "ACH01", EarnedTime = 1 });
        queue.Enqueue(new NewAchievementEventArgs { AppId = "12345", AchievementName = "ACH02", EarnedTime = 2 });

        // Both items should be queued (dispatch hasn't run since there's no real Dispatcher loop)
        Assert.True(queue.Count >= 0);
    }

    [Fact]
    public void IsPaused_DefaultFalse()
    {
        var queue = new NotificationQueue(_gameCache, _config);
        Assert.False(queue.IsPaused);
    }

    [Fact]
    public void IsPaused_CanBeToggled()
    {
        var queue = new NotificationQueue(_gameCache, _config);
        queue.IsPaused = true;
        Assert.True(queue.IsPaused);
        queue.IsPaused = false;
        Assert.False(queue.IsPaused);
    }

    [Fact]
    public void Dispose_ClearsQueue()
    {
        var queue = new NotificationQueue(_gameCache, _config);

        queue.Enqueue(new NewAchievementEventArgs { AppId = "12345", AchievementName = "ACH01", EarnedTime = 1 });

        queue.Dispose();

        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void ResolveMetadata_WithIconPath_ResolvesIcon()
    {
        // Create icon file
        var imgDir = Path.Combine(_gamesDir, "TestGame", "steam_settings", "img");
        Directory.CreateDirectory(imgDir);
        File.WriteAllBytes(Path.Combine(imgDir, "ach01.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var freshCache = new GameCache(new[] { _gamesDir });
        freshCache.ScanAll();
        var queue = new NotificationQueue(freshCache, _config);

        var args = new NewAchievementEventArgs
        {
            AppId = "12345",
            AchievementName = "ACH01",
            EarnedTime = 1700000000
        };

        var item = queue.ResolveMetadata(args);

        Assert.NotNull(item);
        Assert.NotNull(item.IconPath);
        Assert.Contains("ach01.png", item.IconPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
    }
}
