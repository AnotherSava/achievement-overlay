using System.IO;
using System.Text.Json;
using AchievementOverlay;
using Xunit;

namespace AchievementOverlay.Tests;

public class AppConfigTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsPath;
    private readonly string _gseSavesDir;

    public AppConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AchievementOverlayTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _settingsPath = Path.Combine(_tempDir, "config.json");
        _gseSavesDir = Path.Combine(_tempDir, "GSE Saves");
        Directory.CreateDirectory(_gseSavesDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void DefaultValues_FromDefaultConfig()
    {
        var defaultJson = $$"""
        {
          "gseSavesPath": "{{_gseSavesDir.Replace("\\", "\\\\")}}",
          "gamesPaths": "C:\\Games",
          "language": "english",
          "soundEnabled": true,
          "soundPath": "",
          "displayDuration": 7,
          "recentAchievementsShortcut": "Ctrl+Shift+H",
          "recentAchievementsCount": 5
        }
        """;
        File.WriteAllText(_settingsPath, defaultJson);
        var config = new AppConfig(_settingsPath);
        var settings = config.GetCurrent();

        Assert.Equal(_gseSavesDir, settings.GseSavesPath);
        Assert.Equal(@"C:\Games", settings.GamesPaths);
        Assert.Equal("english", settings.Language);
        Assert.True(settings.SoundEnabled);
        Assert.Equal("", settings.SoundPath);
        Assert.Equal(7, settings.DisplayDuration);
        Assert.Equal("Ctrl+Shift+H", settings.RecentAchievementsShortcut);
        Assert.Equal(5, settings.RecentAchievementsCount);
    }

    [Fact]
    public void Load_ReadsExistingSettingsFile()
    {
        var data = new
        {
            gseSavesPath = _gseSavesDir,
            gamesPaths = @"C:\Games;D:\MoreGames",
            language = "german",
            soundEnabled = false,
            soundPath = @"C:\sound.wav",
            displayDuration = 7,
            recentAchievementsShortcut = "Ctrl+Shift+H",
            recentAchievementsCount = 5
        };
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(data));

        var config = new AppConfig(_settingsPath);
        var settings = config.GetCurrent();

        Assert.Equal(_gseSavesDir, settings.GseSavesPath);
        Assert.Equal(@"C:\Games;D:\MoreGames", settings.GamesPaths);
        Assert.Equal("german", settings.Language);
        Assert.False(settings.SoundEnabled);
        Assert.Equal(@"C:\sound.wav", settings.SoundPath);
    }

    [Fact]
    public void ExpandEnvironmentVariables_ExpandsAppdata()
    {
        var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var result = AppConfig.ExpandEnvironmentVariables(@"%appdata%\GSE Saves");
        Assert.Equal(Path.Combine(appdata, "GSE Saves"), result);
    }

    [Fact]
    public void ExpandEnvironmentVariables_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", AppConfig.ExpandEnvironmentVariables(""));
    }

    [Fact]
    public void ExpandEnvironmentVariables_NullString_ReturnsNull()
    {
        Assert.Null(AppConfig.ExpandEnvironmentVariables(null!));
    }

    [Fact]
    public void ParseGamesPaths_SemicolonSeparated_ReturnsSplitArray()
    {
        var result = AppConfig.ParseGamesPaths(@"C:\Games;D:\MoreGames;E:\Steam");
        Assert.Equal(3, result.Length);
        Assert.Contains(@"C:\Games", result);
        Assert.Contains(@"D:\MoreGames", result);
        Assert.Contains(@"E:\Steam", result);
    }

    [Fact]
    public void ParseGamesPaths_EmptyString_ReturnsEmptyArray()
    {
        var result = AppConfig.ParseGamesPaths("");
        Assert.Empty(result);
    }

    [Fact]
    public void ParseGamesPaths_Null_ReturnsEmptyArray()
    {
        var result = AppConfig.ParseGamesPaths(null);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseGamesPaths_TrailingSemicolon_IgnoresEmpty()
    {
        var result = AppConfig.ParseGamesPaths(@"C:\Games;");
        Assert.Single(result);
        Assert.Equal(@"C:\Games", result[0]);
    }

    [Fact]
    public void ParseGamesPaths_WhitespaceEntries_Trimmed()
    {
        var result = AppConfig.ParseGamesPaths(@"  C:\Games  ;  D:\More  ");
        Assert.Equal(2, result.Length);
        Assert.Equal(@"C:\Games", result[0]);
        Assert.Equal(@"D:\More", result[1]);
    }

    [Fact]
    public void ParseGamesPaths_ExpandsEnvironmentVariables()
    {
        var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var result = AppConfig.ParseGamesPaths(@"%appdata%\Games");
        Assert.Single(result);
        Assert.Equal(Path.Combine(appdata, "Games"), result[0]);
    }

    [Fact]
    public void MissingFile_ThrowsFileNotFoundException()
    {
        Assert.False(File.Exists(_settingsPath));
        Assert.Throws<FileNotFoundException>(() => new AppConfig(_settingsPath));
    }

    [Fact]
    public void HotReload_DetectsFileChanges()
    {
        var initialData = new { gseSavesPath = _gseSavesDir, gamesPaths = @"C:\Games", language = "english", soundEnabled = true, soundPath = "", displayDuration = 7, recentAchievementsShortcut = "Ctrl+Shift+H", recentAchievementsCount = 5 };
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(initialData));

        var config = new AppConfig(_settingsPath);
        Assert.True(config.GetCurrent().SoundEnabled);

        // Wait a bit to ensure different timestamp
        Thread.Sleep(50);

        // Modify the file externally
        var updatedData = new { gseSavesPath = _gseSavesDir, gamesPaths = @"C:\Games", language = "english", soundEnabled = false, soundPath = "", displayDuration = 7, recentAchievementsShortcut = "Ctrl+Shift+H", recentAchievementsCount = 5 };
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(updatedData));

        // Force a different write time
        File.SetLastWriteTimeUtc(_settingsPath, DateTime.UtcNow.AddSeconds(1));

        var settings = config.GetCurrent();
        Assert.False(settings.SoundEnabled);
    }

    [Fact]
    public void UpdateConfigValue_UpdatesSingleProperty()
    {
        var initialData = new { gseSavesPath = _gseSavesDir, gamesPaths = @"C:\Games", language = "english", soundEnabled = true, soundPath = "", displayDuration = 7, recentAchievementsShortcut = "Ctrl+Shift+H", recentAchievementsCount = 5 };
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(initialData));

        var config = new AppConfig(_settingsPath);
        Assert.True(config.GetCurrent().SoundEnabled);

        config.UpdateConfigValue("SoundEnabled", false, _settingsPath);

        Assert.False(config.GetCurrent().SoundEnabled);

        // Verify other properties preserved
        Assert.Equal("english", config.GetCurrent().Language);
    }

    [Fact]
    public void UpdateConfigValue_PreservesOtherProperties()
    {
        var initialData = new { gseSavesPath = _gseSavesDir, gamesPaths = @"C:\Games", language = "german", soundEnabled = true, soundPath = @"C:\beep.wav", displayDuration = 7, recentAchievementsShortcut = "Ctrl+Shift+H", recentAchievementsCount = 5 };
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(initialData));

        var config = new AppConfig(_settingsPath);
        config.UpdateConfigValue("Language", "french", _settingsPath);

        var settings = config.GetCurrent();
        Assert.Equal("french", settings.Language);
        Assert.Equal(_gseSavesDir, settings.GseSavesPath);
        Assert.Equal(@"C:\Games", settings.GamesPaths);
        Assert.True(settings.SoundEnabled);
        Assert.Equal(@"C:\beep.wav", settings.SoundPath);
    }

    [Fact]
    public void UpdateConfigValue_WritesValidJson()
    {
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new { gseSavesPath = _gseSavesDir, gamesPaths = @"C:\Games", language = "english", soundEnabled = true, soundPath = "", displayDuration = 7, recentAchievementsShortcut = "Ctrl+Shift+H", recentAchievementsCount = 5 }));

        var config = new AppConfig(_settingsPath);
        config.UpdateConfigValue("SoundEnabled", false, _settingsPath);

        // The file should be valid JSON
        var json = File.ReadAllText(_settingsPath);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        Assert.NotNull(parsed);
        Assert.Equal(JsonValueKind.False, parsed!["soundEnabled"].ValueKind);
    }

    [Fact]
    public void GseSavesPath_ExpandsEnvironmentVariables()
    {
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new { gseSavesPath = _gseSavesDir, gamesPaths = @"C:\Games", language = "english", soundEnabled = true, soundPath = "", displayDuration = 7, recentAchievementsShortcut = "Ctrl+Shift+H", recentAchievementsCount = 5 }));

        var config = new AppConfig(_settingsPath);
        Assert.Equal(_gseSavesDir, config.GseSavesPath);
    }

    [Fact]
    public void GamesPaths_ParsesSemicolonSeparated()
    {
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new { gseSavesPath = _gseSavesDir, gamesPaths = @"C:\Games;D:\More", language = "english", soundEnabled = true, soundPath = "", displayDuration = 7, recentAchievementsShortcut = "Ctrl+Shift+H", recentAchievementsCount = 5 }));

        var config = new AppConfig(_settingsPath);
        Assert.Equal(2, config.GamesPaths.Length);
        Assert.Equal(@"C:\Games", config.GamesPaths[0]);
        Assert.Equal(@"D:\More", config.GamesPaths[1]);
    }
}
