using System.IO;
using AchievementOverlay;
using Xunit;

namespace AchievementOverlay.Tests;

public class GameCacheTests : IDisposable
{
    private readonly string _tempDir;
    private readonly List<string> _logMessages = new();

    public GameCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GameCacheTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private void Log(string msg) => _logMessages.Add(msg);

    /// <summary>
    /// Creates a fake game directory structure with steam_appid.txt and optionally
    /// steam_settings/achievements.json.
    /// </summary>
    private string CreateGameDir(string gameName, string appId, string? achievementsJson = null)
    {
        var gameDir = Path.Combine(_tempDir, "games", gameName);
        Directory.CreateDirectory(gameDir);

        File.WriteAllText(Path.Combine(gameDir, "steam_appid.txt"), appId);

        if (achievementsJson != null)
        {
            var settingsDir = Path.Combine(gameDir, "steam_settings");
            Directory.CreateDirectory(settingsDir);
            File.WriteAllText(Path.Combine(settingsDir, "achievements.json"), achievementsJson);
        }

        return gameDir;
    }

    // --- ScanAll tests ---

    [Fact]
    public void ScanAll_FindsGamesWithAchievements()
    {
        var achievementsJson = """[{"name": "ACH01", "displayName": "Test"}]""";
        CreateGameDir("Game1", "12345", achievementsJson);
        CreateGameDir("Game2", "67890", achievementsJson);

        var gamesPath = Path.Combine(_tempDir, "games");
        var cache = new GameCache(new[] { gamesPath }, Log);
        cache.ScanAll();

        Assert.True(cache.Contains("12345"));
        Assert.True(cache.Contains("67890"));
        Assert.Equal(2, cache.GetAll().Count);
    }

    [Fact]
    public void ScanAll_SkipsGamesWithoutAchievementsJson()
    {
        var achievementsJson = """[{"name": "ACH01", "displayName": "Test"}]""";
        CreateGameDir("GameWithAch", "11111", achievementsJson);
        CreateGameDir("GameWithoutAch", "22222"); // no achievements.json

        var gamesPath = Path.Combine(_tempDir, "games");
        var cache = new GameCache(new[] { gamesPath }, Log);
        cache.ScanAll();

        Assert.True(cache.Contains("11111"));
        Assert.False(cache.Contains("22222"));
        Assert.Single(cache.GetAll());
    }

    [Fact]
    public void ScanAll_SkipsNonExistentPaths()
    {
        var fakePath = Path.Combine(_tempDir, "nonexistent");
        var cache = new GameCache(new[] { fakePath }, Log, Log);
        cache.ScanAll();

        Assert.Empty(cache.GetAll());
        Assert.Contains(_logMessages, m => m.Contains("does not exist"));
    }

    [Fact]
    public void ScanAll_EmptyGamesPaths_NoError()
    {
        var cache = new GameCache(Array.Empty<string>(), Log);
        cache.ScanAll();
        Assert.Empty(cache.GetAll());
    }

    [Fact]
    public void ScanAll_LogsDetectedGames()
    {
        var achievementsJson = """[{"name": "ACH01", "displayName": "Test"}]""";
        CreateGameDir("MyGame", "99999", achievementsJson);

        var gamesPath = Path.Combine(_tempDir, "games");
        var cache = new GameCache(new[] { gamesPath }, Log);
        cache.ScanAll();

        Assert.Contains(_logMessages, m => m.Contains("99999"));
        Assert.Contains(_logMessages, m => m.Contains("scan complete"));
    }

    // --- Lookup tests ---

    [Fact]
    public void Lookup_CachedAppId_ReturnsGameInfo()
    {
        var achievementsJson = """[{"name": "ACH01", "displayName": "Test"}]""";
        CreateGameDir("Game1", "12345", achievementsJson);

        var gamesPath = Path.Combine(_tempDir, "games");
        var cache = new GameCache(new[] { gamesPath }, Log);
        cache.ScanAll();

        var info = cache.Lookup("12345");
        Assert.NotNull(info);
        Assert.Equal("12345", info!.AppId);
        Assert.Equal("Game1", info.GameName);
        Assert.True(File.Exists(info.MetadataPath));
    }

    [Fact]
    public void Lookup_UnknownAppId_TriggersRescanAndReturnsNull()
    {
        var gamesPath = Path.Combine(_tempDir, "games");
        Directory.CreateDirectory(gamesPath);
        var cache = new GameCache(new[] { gamesPath }, Log);
        cache.ScanAll();

        var info = cache.Lookup("99999");
        Assert.Null(info);
        Assert.Contains(_logMessages, m => m.Contains("re-scanning"));
    }

    [Fact]
    public void Lookup_NewGameAppears_RescanFindsIt()
    {
        var gamesPath = Path.Combine(_tempDir, "games");
        Directory.CreateDirectory(gamesPath);
        var cache = new GameCache(new[] { gamesPath }, Log);
        cache.ScanAll();

        // Initially not found
        Assert.False(cache.Contains("55555"));

        // Now add a game
        var achievementsJson = """[{"name": "ACH01", "displayName": "Test"}]""";
        CreateGameDir("NewGame", "55555", achievementsJson);

        // Lookup triggers re-scan
        var info = cache.Lookup("55555");
        Assert.NotNull(info);
        Assert.Equal("55555", info!.AppId);
    }

    // --- GameInfo tests ---

    [Fact]
    public void GameInfo_MetadataPath_PointsToCorrectFile()
    {
        var achievementsJson = """[{"name": "ACH01", "displayName": "Test"}]""";
        var gameDir = CreateGameDir("TestGame", "44444", achievementsJson);

        var gamesPath = Path.Combine(_tempDir, "games");
        var cache = new GameCache(new[] { gamesPath }, Log);
        cache.ScanAll();

        var info = cache.Lookup("44444");
        Assert.NotNull(info);
        Assert.Equal(Path.Combine(gameDir, "steam_settings", "achievements.json"), info!.MetadataPath);
    }

    // --- LoadDefinitions tests ---

    [Fact]
    public void LoadDefinitions_ValidFile_ReturnsParsedList()
    {
        var achievementsJson = """
        [
            {"name": "ACH01", "displayName": "First", "description": "Do first thing"},
            {"name": "ACH02", "displayName": "Second", "description": "Do second thing"}
        ]
        """;
        CreateGameDir("Game1", "11111", achievementsJson);

        var gamesPath = Path.Combine(_tempDir, "games");
        var cache = new GameCache(new[] { gamesPath }, Log);
        cache.ScanAll();

        var info = cache.Lookup("11111");
        var defs = GameCache.LoadDefinitions(info!);

        Assert.NotNull(defs);
        Assert.Equal(2, defs!.Count);
        Assert.Equal("ACH01", defs[0].Name);
        Assert.Equal("ACH02", defs[1].Name);
    }

    // --- Multiple game paths ---

    [Fact]
    public void ScanAll_MultipleGamesPaths_FindsAll()
    {
        var achievementsJson = """[{"name": "ACH01", "displayName": "Test"}]""";

        var path1 = Path.Combine(_tempDir, "path1");
        var path2 = Path.Combine(_tempDir, "path2");
        Directory.CreateDirectory(path1);
        Directory.CreateDirectory(path2);

        // Create games in different base paths
        var game1Dir = Path.Combine(path1, "Game1");
        Directory.CreateDirectory(game1Dir);
        File.WriteAllText(Path.Combine(game1Dir, "steam_appid.txt"), "11111");
        var ss1 = Path.Combine(game1Dir, "steam_settings");
        Directory.CreateDirectory(ss1);
        File.WriteAllText(Path.Combine(ss1, "achievements.json"), achievementsJson);

        var game2Dir = Path.Combine(path2, "Game2");
        Directory.CreateDirectory(game2Dir);
        File.WriteAllText(Path.Combine(game2Dir, "steam_appid.txt"), "22222");
        var ss2 = Path.Combine(game2Dir, "steam_settings");
        Directory.CreateDirectory(ss2);
        File.WriteAllText(Path.Combine(ss2, "achievements.json"), achievementsJson);

        var cache = new GameCache(new[] { path1, path2 }, Log);
        cache.ScanAll();

        Assert.True(cache.Contains("11111"));
        Assert.True(cache.Contains("22222"));
        Assert.Equal(2, cache.GetAll().Count);
    }

    // --- Edge case: whitespace/newline in steam_appid.txt ---

    [Fact]
    public void ScanAll_AppIdWithWhitespace_TrimsCorrectly()
    {
        var achievementsJson = """[{"name": "ACH01", "displayName": "Test"}]""";
        var gameDir = Path.Combine(_tempDir, "games", "TrimGame");
        Directory.CreateDirectory(gameDir);
        // Write appid with trailing newline and spaces
        File.WriteAllText(Path.Combine(gameDir, "steam_appid.txt"), "  33333  \n");
        var ss = Path.Combine(gameDir, "steam_settings");
        Directory.CreateDirectory(ss);
        File.WriteAllText(Path.Combine(ss, "achievements.json"), achievementsJson);

        var gamesPath = Path.Combine(_tempDir, "games");
        var cache = new GameCache(new[] { gamesPath }, Log);
        cache.ScanAll();

        Assert.True(cache.Contains("33333"));
    }
}
