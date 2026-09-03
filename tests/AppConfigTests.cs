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
          "gseSavesPaths": "{{_gseSavesDir.Replace("\\", "\\\\")}}",
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

        Assert.Equal(_gseSavesDir, settings.GseSavesPaths);
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
            gseSavesPaths = _gseSavesDir,
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

        Assert.Equal(_gseSavesDir, settings.GseSavesPaths);
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
    public void CollapseEnvironmentVariables_PathUnderAppData_UsesVariable()
    {
        var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        Assert.Equal(@"%appdata%\GSE Saves", AppConfig.CollapseEnvironmentVariables(Path.Combine(appdata, "GSE Saves")));
    }

    [Fact]
    public void CollapseEnvironmentVariables_FolderItself_BecomesBareVariable()
    {
        var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        Assert.Equal("%appdata%", AppConfig.CollapseEnvironmentVariables(appdata));
    }

    [Fact]
    public void CollapseEnvironmentVariables_PrefersTheDeepestFolder()
    {
        // LocalApplicationData sits under UserProfile, so the shorter match must not win.
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.Equal(@"%localappdata%\Games", AppConfig.CollapseEnvironmentVariables(Path.Combine(localAppData, "Games")));
    }

    [Fact]
    public void CollapseEnvironmentVariables_UnrelatedPath_IsUnchanged()
    {
        Assert.Equal(@"D:\Games\Atomfall", AppConfig.CollapseEnvironmentVariables(@"D:\Games\Atomfall"));
    }

    [Fact]
    public void CollapseEnvironmentVariables_SiblingWithSharedPrefix_IsUnchanged()
    {
        // A sibling of the profile folder shares its whole text but is not inside it: 'C:\Users\Bobby'
        // must not collapse against 'C:\Users\Bob'. Built from the profile because it is the outermost
        // collapsible folder, so nothing else can legitimately claim the result.
        var sibling = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "Extra";

        Assert.Equal(sibling, AppConfig.CollapseEnvironmentVariables(sibling));
    }

    [Fact]
    public void CollapseEnvironmentVariables_RoundTripsThroughExpand()
    {
        // The whole point: what the dialog stores has to read back as the folder that was picked.
        var picked = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GSE Saves");

        var collapsed = AppConfig.CollapseEnvironmentVariables(picked);

        Assert.NotEqual(picked, collapsed);
        Assert.Equal(picked, AppConfig.ExpandEnvironmentVariables(collapsed));
    }

    [Fact]
    public void SplitRawPaths_KeepsEnvironmentVariablesUnexpanded()
    {
        // The settings window round-trips these straight back into config, so expanding here would
        // freeze a portable '%appdata%\GSE Saves' into one machine's absolute path on the first save.
        var result = AppConfig.SplitRawPaths(@"%appdata%\GSE Saves");

        Assert.Single(result);
        Assert.Equal(@"%appdata%\GSE Saves", result[0]);
    }

    [Fact]
    public void SplitRawPaths_SplitsOnSemicolonAndTrims()
    {
        var result = AppConfig.SplitRawPaths(@"  C:\Games ;  D:\More  ");

        Assert.Equal(2, result.Length);
        Assert.Equal(@"C:\Games", result[0]);
        Assert.Equal(@"D:\More", result[1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SplitRawPaths_EmptyValue_ReturnsNoEntries(string? value)
    {
        // A user whose games all describe their own achievements has no game roots at all.
        Assert.Empty(AppConfig.SplitRawPaths(value));
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
        var initialData = new { gseSavesPaths = _gseSavesDir, gamesPaths = @"C:\Games", language = "english", soundEnabled = true, soundPath = "", displayDuration = 7, recentAchievementsShortcut = "Ctrl+Shift+H", recentAchievementsCount = 5 };
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(initialData));

        var config = new AppConfig(_settingsPath);
        Assert.True(config.GetCurrent().SoundEnabled);

        // Wait a bit to ensure different timestamp
        Thread.Sleep(50);

        // Modify the file externally
        var updatedData = new { gseSavesPaths = _gseSavesDir, gamesPaths = @"C:\Games", language = "english", soundEnabled = false, soundPath = "", displayDuration = 7, recentAchievementsShortcut = "Ctrl+Shift+H", recentAchievementsCount = 5 };
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(updatedData));

        // Force a different write time
        File.SetLastWriteTimeUtc(_settingsPath, DateTime.UtcNow.AddSeconds(1));

        var settings = config.GetCurrent();
        Assert.False(settings.SoundEnabled);
    }

    [Fact]
    public void HotReload_InvalidSettings_KeepsLastGoodConfig()
    {
        var initialData = new { gseSavesPaths = _gseSavesDir, gamesPaths = @"C:\Games", language = "english", soundEnabled = true, soundPath = "", displayDuration = 7, recentAchievementsShortcut = "Ctrl+Shift+H", recentAchievementsCount = 5 };
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(initialData));

        var config = new AppConfig(_settingsPath);
        Assert.Equal(5, config.RecentAchievementsCount);

        // The user saves an intermediate state while hand-editing: valid JSON, but a required
        // setting is momentarily gone. Reading a property must not take the app down.
        Thread.Sleep(50);
        var midEdit = new { gseSavesPaths = _gseSavesDir, gamesPaths = @"C:\Games", language = "english", soundEnabled = true, soundPath = "", displayDuration = 7, recentAchievementsShortcut = "Ctrl+Shift+H" };
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(midEdit));
        File.SetLastWriteTimeUtc(_settingsPath, DateTime.UtcNow.AddSeconds(1));

        Assert.Equal(5, config.RecentAchievementsCount);
        Assert.Equal("english", config.Language);
    }

    [Fact]
    public void HotReload_MalformedJson_KeepsLastGoodConfig()
    {
        var initialData = new { gseSavesPaths = _gseSavesDir, gamesPaths = @"C:\Games", language = "english", soundEnabled = true, soundPath = "", displayDuration = 7, recentAchievementsShortcut = "Ctrl+Shift+H", recentAchievementsCount = 5 };
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(initialData));

        var config = new AppConfig(_settingsPath);
        Assert.Equal("english", config.Language);

        Thread.Sleep(50);
        File.WriteAllText(_settingsPath, "{ not valid json");
        File.SetLastWriteTimeUtc(_settingsPath, DateTime.UtcNow.AddSeconds(1));

        Assert.Equal("english", config.Language);
    }

    [Fact]
    public void UpdateConfigValue_UpdatesSingleProperty()
    {
        var initialData = new { gseSavesPaths = _gseSavesDir, gamesPaths = @"C:\Games", language = "english", soundEnabled = true, soundPath = "", displayDuration = 7, recentAchievementsShortcut = "Ctrl+Shift+H", recentAchievementsCount = 5 };
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
        var initialData = new { gseSavesPaths = _gseSavesDir, gamesPaths = @"C:\Games", language = "german", soundEnabled = true, soundPath = @"C:\beep.wav", displayDuration = 7, recentAchievementsShortcut = "Ctrl+Shift+H", recentAchievementsCount = 5 };
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(initialData));

        var config = new AppConfig(_settingsPath);
        config.UpdateConfigValue("Language", "french", _settingsPath);

        var settings = config.GetCurrent();
        Assert.Equal("french", settings.Language);
        Assert.Equal(_gseSavesDir, settings.GseSavesPaths);
        Assert.Equal(@"C:\Games", settings.GamesPaths);
        Assert.True(settings.SoundEnabled);
        Assert.Equal(@"C:\beep.wav", settings.SoundPath);
    }

    [Fact]
    public void UpdateConfigValue_WritesValidJson()
    {
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new { gseSavesPaths = _gseSavesDir, gamesPaths = @"C:\Games", language = "english", soundEnabled = true, soundPath = "", displayDuration = 7, recentAchievementsShortcut = "Ctrl+Shift+H", recentAchievementsCount = 5 }));

        var config = new AppConfig(_settingsPath);
        config.UpdateConfigValue("SoundEnabled", false, _settingsPath);

        // The file should be valid JSON
        var json = File.ReadAllText(_settingsPath);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        Assert.NotNull(parsed);
        Assert.Equal(JsonValueKind.False, parsed!["soundEnabled"].ValueKind);
    }

    [Fact]
    public void UpdateConfigValues_WritesEveryChangedKeyInOnePass()
    {
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new { gseSavesPaths = _gseSavesDir, gamesPaths = @"C:\Games", language = "english", soundEnabled = true, soundPath = "", displayDuration = 7, recentAchievementsShortcut = "Ctrl+Shift+H", recentAchievementsCount = 5 }));

        var config = new AppConfig(_settingsPath);
        config.UpdateConfigValues(new Dictionary<string, object?>
        {
            ["Language"] = "german",
            ["DisplayDuration"] = 12,
            ["SoundEnabled"] = false,
            ["SteamWebApiKey"] = "key"
        }, _settingsPath);

        var settings = config.GetCurrent();
        Assert.Equal("german", settings.Language);
        Assert.Equal(12, settings.DisplayDuration);
        Assert.False(settings.SoundEnabled);
        Assert.Equal("key", settings.SteamWebApiKey);

        // Untouched keys survive, and the file is still valid JSON.
        Assert.Equal(@"C:\Games", settings.GamesPaths);
        Assert.Equal(5, settings.RecentAchievementsCount);
        Assert.NotNull(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(_settingsPath)));
    }

    [Fact]
    public void UpdateConfigValues_RoundTripsAScaleSavedFromTheSettingsWindow()
    {
        // The settings window boxes a NotificationScale into the object dictionary, so it is
        // serialized on its own rather than as a property of SettingsData. With the converter
        // attached to the property instead of the type it wrote {"Unit":0,"Value":15} and threw
        // "Cannot convert token StartObject to a scale" on the read back — after the file was
        // already written, leaving a config the app could not start from.
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new { gseSavesPaths = _gseSavesDir, gamesPaths = @"C:\Games", language = "english", soundEnabled = true, soundPath = "", displayDuration = 7, recentAchievementsShortcut = "Ctrl+Shift+H", recentAchievementsCount = 5 }));

        var config = new AppConfig(_settingsPath);
        config.UpdateConfigValues(new Dictionary<string, object?> { ["Scale"] = NotificationScale.Pixels(420) }, _settingsPath);

        var saved = config.GetCurrent().Scale;
        Assert.Equal(ScaleUnit.Pixels, saved.Unit);
        Assert.Equal(420, saved.Value);
        Assert.Contains("\"420px\"", File.ReadAllText(_settingsPath));
    }

    [Fact]
    public void UpdateConfigValues_RoundTripsAPositionSavedFromTheSettingsWindow()
    {
        // Same trap as the scale above: the value is boxed into the object dictionary and serialized
        // on its own, so only a type-level converter applies. Without one a bare enum writes 3.
        File.WriteAllText(_settingsPath, MinimalConfigJson());

        var config = new AppConfig(_settingsPath);
        config.UpdateConfigValues(new Dictionary<string, object?> { ["NotificationPosition"] = NotificationAnchor.TopRight }, _settingsPath);

        Assert.Equal(NotificationAnchor.TopRight, config.GetCurrent().NotificationPosition);
        Assert.Contains("\"top_right\"", File.ReadAllText(_settingsPath));
    }

    [Fact]
    public void UpdateConfigValues_RoundTripsABackgroundSavedFromTheSettingsWindow()
    {
        File.WriteAllText(_settingsPath, MinimalConfigJson());

        var config = new AppConfig(_settingsPath);
        config.UpdateConfigValues(new Dictionary<string, object?> { ["NotificationBackground"] = PopupBackground.Parse("#FFF5F5F0") }, _settingsPath);

        Assert.Equal("#FFF5F5F0", config.GetCurrent().NotificationBackground.ToString());
        Assert.Contains("\"#FFF5F5F0\"", File.ReadAllText(_settingsPath));
    }

    [Theory]
    [InlineData("\"nonsense\"")]
    [InlineData("true")]
    [InlineData("17")]
    [InlineData("{}")]
    public void HandEditedBackgroundJunk_StillStarts(string value)
    {
        File.WriteAllText(_settingsPath, MinimalConfigJson(extra: $"\"notificationBackground\": {value},"));

        Assert.Equal(PopupBackground.Default, new AppConfig(_settingsPath).GetCurrent().NotificationBackground);
    }

    [Fact]
    public void MissingBackground_ReadsAsTheShippedFill()
    {
        File.WriteAllText(_settingsPath, MinimalConfigJson());

        Assert.Equal(PopupBackground.Default, new AppConfig(_settingsPath).GetCurrent().NotificationBackground);
    }

    [Fact]
    public void MissingPosition_ReadsAsBottomRight()
    {
        // Every config that exists today is missing the key, and none of those installs may move.
        File.WriteAllText(_settingsPath, MinimalConfigJson());

        Assert.Equal(NotificationAnchor.BottomRight, new AppConfig(_settingsPath).GetCurrent().NotificationPosition);
    }

    [Theory]
    [InlineData("\"sideways\"")]
    [InlineData("true")]
    [InlineData("7")]
    [InlineData("{}")]
    public void HandEditedPositionJunk_StillStarts(string value)
    {
        // The path this guards is TrayApplicationContext's constructor catch, which turns a JsonException
        // out of the load into the config-error dialog and no app at all.
        File.WriteAllText(_settingsPath, MinimalConfigJson(extra: $"\"notificationPosition\": {value},"));

        var config = new AppConfig(_settingsPath);

        Assert.Equal(NotificationAnchor.BottomRight, config.GetCurrent().NotificationPosition);
    }

    private string MinimalConfigJson(string extra = "") =>
        $$"""
        {
          {{extra}}
          "gseSavesPaths": "{{_gseSavesDir.Replace("\\", "\\\\")}}",
          "gamesPaths": "C:\\Games",
          "language": "english",
          "soundEnabled": true,
          "soundPath": "",
          "displayDuration": 7,
          "recentAchievementsShortcut": "Ctrl+Shift+H",
          "recentAchievementsCount": 5
        }
        """;

    [Fact]
    public void UpdateConfigValues_PreservesAppManagedState()
    {
        // trackingConfigured isn't editable in the settings dialog, so a save must leave it alone.
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new { gseSavesPaths = _gseSavesDir, gamesPaths = @"C:\Games", language = "english", soundEnabled = true, soundPath = "", displayDuration = 7, recentAchievementsShortcut = "Ctrl+Shift+H", recentAchievementsCount = 5, trackingConfigured = new Dictionary<string, long> { ["1601580"] = 1755000000 } }));

        var config = new AppConfig(_settingsPath);
        config.UpdateConfigValues(new Dictionary<string, object?> { ["Language"] = "french" }, _settingsPath);

        var tracking = config.GetCurrent().TrackingConfigured;
        Assert.NotNull(tracking);
        Assert.Equal(1755000000, tracking!["1601580"]);
    }

    [Fact]
    public void GseSavesPaths_ExpandsEnvironmentVariables()
    {
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new { gseSavesPaths = _gseSavesDir, gamesPaths = @"C:\Games", language = "english", soundEnabled = true, soundPath = "", displayDuration = 7, recentAchievementsShortcut = "Ctrl+Shift+H", recentAchievementsCount = 5 }));

        var config = new AppConfig(_settingsPath);
        Assert.Equal(_gseSavesDir, config.GseSavesPaths[0]);
    }

    [Fact]
    public void GamesPaths_ParsesSemicolonSeparated()
    {
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new { gseSavesPaths = _gseSavesDir, gamesPaths = @"C:\Games;D:\More", language = "english", soundEnabled = true, soundPath = "", displayDuration = 7, recentAchievementsShortcut = "Ctrl+Shift+H", recentAchievementsCount = 5 }));

        var config = new AppConfig(_settingsPath);
        Assert.Equal(2, config.GamesPaths.Length);
        Assert.Equal(@"C:\Games", config.GamesPaths[0]);
        Assert.Equal(@"D:\More", config.GamesPaths[1]);
    }

    [Fact]
    public void EmptyGamesPaths_IsAccepted()
    {
        // A user whose games all describe their own achievements has no Steam game roots.
        var data = new { gseSavesPaths = _gseSavesDir, gamesPaths = "", language = "english", soundEnabled = true, soundPath = "", displayDuration = 7, recentAchievementsShortcut = "Ctrl+Shift+H", recentAchievementsCount = 5 };
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(data));

        var config = new AppConfig(_settingsPath);

        Assert.Empty(config.GamesPaths);
    }

    [Fact]
    public void MissingGamesPathsKey_Throws()
    {
        // An absent key is still a config error, so a typo'd key stays loud.
        var data = new { gseSavesPaths = _gseSavesDir, language = "english", soundEnabled = true, soundPath = "", displayDuration = 7, recentAchievementsShortcut = "Ctrl+Shift+H", recentAchievementsCount = 5 };
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(data));

        var ex = Assert.Throws<InvalidOperationException>(() => new AppConfig(_settingsPath));
        Assert.Contains("gamesPaths", ex.Message);
    }

    [Fact]
    public void CollapseEnvironmentVariablesInText_HidesTheAccountNameInsideALogLine()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var line = $"[INFO] Watching for achievements in '{appData}" + @"\GSE Saves'";

        var collapsed = AppConfig.CollapseEnvironmentVariablesInText(line);

        Assert.Equal(@"[INFO] Watching for achievements in '%appdata%\GSE Saves'", collapsed);
        Assert.DoesNotContain(Environment.UserName, collapsed, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CollapseEnvironmentVariablesInText_PrefersTheDeepestFolder()
    {
        // %appdata% sits under %userprofile%; taking the shallower one would leave the account name in.
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        Assert.StartsWith("%appdata%", AppConfig.CollapseEnvironmentVariablesInText(appData + @"\GSE Saves"));
    }

    [Fact]
    public void CollapseEnvironmentVariablesInText_LeavesUnrelatedTextAlone() =>
        Assert.Equal(@"[INFO] Cached: appid=812140, path='C:\Games\Odyssey'",
            AppConfig.CollapseEnvironmentVariablesInText(@"[INFO] Cached: appid=812140, path='C:\Games\Odyssey'"));
}
