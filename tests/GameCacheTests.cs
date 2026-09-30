using System.IO;
using Xunit;

namespace AchievementOverlay.Tests;

[Collection("App log")]
public sealed class GameCacheTests : IDisposable
{
    private readonly string _tempDir;

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

    /// <summary>
    /// The app log's lines that name this test's folder. It is named by a fresh GUID, so lines left by
    /// earlier runs never match.
    /// </summary>
    private List<string> LogLines() => File.ReadLines(Logger.LogPath).Where(line => line.Contains(_tempDir, StringComparison.Ordinal)).ToList();

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
        var cache = new GameCache(new[] { gamesPath });
        cache.ScanAll();

        Assert.True(cache.LookupCached("12345") != null);
        Assert.True(cache.LookupCached("67890") != null);
        Assert.Equal(2, cache.GetAll().Count);
    }

    [Fact]
    public void ScanAll_SkipsGamesWithoutAchievementsJson()
    {
        var achievementsJson = """[{"name": "ACH01", "displayName": "Test"}]""";
        CreateGameDir("GameWithAch", "11111", achievementsJson);
        CreateGameDir("GameWithoutAch", "22222"); // no achievements.json

        var gamesPath = Path.Combine(_tempDir, "games");
        var cache = new GameCache(new[] { gamesPath });
        cache.ScanAll();

        Assert.True(cache.LookupCached("11111") != null);
        Assert.False(cache.LookupCached("22222") != null);
        Assert.Single(cache.GetAll());
    }

    [Fact]
    public void ScanAll_SkipsNonExistentPaths()
    {
        var fakePath = Path.Combine(_tempDir, "nonexistent");
        var cache = new GameCache(new[] { fakePath });
        cache.ScanAll();

        Assert.Empty(cache.GetAll());
    }

    [Fact]
    public void ScanAll_EmptyGamesPaths_NoError()
    {
        var cache = new GameCache(Array.Empty<string>());
        cache.ScanAll();
        Assert.Empty(cache.GetAll());
    }

    [Fact]
    public void ScanAll_LogsDetectedGames()
    {
        var achievementsJson = """[{"name": "ACH01", "displayName": "Test"}]""";
        CreateGameDir("MyGame", "99999", achievementsJson);

        var gamesPath = Path.Combine(_tempDir, "games");
        var cache = new GameCache(new[] { gamesPath });
        cache.ScanAll();

        Assert.True(cache.LookupCached("99999") != null);
    }

    // --- Lookup tests ---

    [Fact]
    public void Lookup_CachedAppId_ReturnsGameInfo()
    {
        var achievementsJson = """[{"name": "ACH01", "displayName": "Test"}]""";
        CreateGameDir("Game1", "12345", achievementsJson);

        var gamesPath = Path.Combine(_tempDir, "games");
        var cache = new GameCache(new[] { gamesPath });
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
        var cache = new GameCache(new[] { gamesPath });
        cache.ScanAll();

        var info = cache.Lookup("99999");
        Assert.Null(info);
        // Lookup triggers a re-scan internally
    }

    [Fact]
    public void Lookup_NewGameAppears_RescanFindsIt()
    {
        var gamesPath = Path.Combine(_tempDir, "games");
        Directory.CreateDirectory(gamesPath);
        var cache = new GameCache(new[] { gamesPath });
        cache.ScanAll();

        // Initially not found
        Assert.False(cache.LookupCached("55555") != null);

        // Now add a game
        var achievementsJson = """[{"name": "ACH01", "displayName": "Test"}]""";
        CreateGameDir("NewGame", "55555", achievementsJson);

        // Lookup triggers re-scan
        var info = cache.Lookup("55555");
        Assert.NotNull(info);
        Assert.Equal("55555", info!.AppId);
    }

    [Fact]
    public void LookupScanningOnce_NewGameAppears_FirstMissRescansAndFindsIt()
    {
        var gamesPath = Path.Combine(_tempDir, "games");
        Directory.CreateDirectory(gamesPath);
        var cache = new GameCache(new[] { gamesPath });
        cache.ScanAll();

        CreateGameDir("NewGame", "55555", """[{"name": "ACH01", "displayName": "Test"}]""");

        Assert.NotNull(cache.LookupScanningOnce("55555"));
    }

    [Fact]
    public void LookupScanningOnce_AppIdAlreadyRescanned_DoesNotRescanAgain()
    {
        var gamesPath = Path.Combine(_tempDir, "games");
        Directory.CreateDirectory(gamesPath);
        var cache = new GameCache(new[] { gamesPath });
        cache.ScanAll();

        // Spends this appid's one rescan while nothing is there to find.
        Assert.Null(cache.LookupScanningOnce("55555"));

        CreateGameDir("NewGame", "55555", """[{"name": "ACH01", "displayName": "Test"}]""");

        Assert.Null(cache.LookupScanningOnce("55555"));
        cache.ScanAll();
        Assert.NotNull(cache.LookupScanningOnce("55555"));
    }

    // --- GameInfo tests ---

    [Fact]
    public void GameInfo_MetadataPath_PointsToCorrectFile()
    {
        var achievementsJson = """[{"name": "ACH01", "displayName": "Test"}]""";
        var gameDir = CreateGameDir("TestGame", "44444", achievementsJson);

        var gamesPath = Path.Combine(_tempDir, "games");
        var cache = new GameCache(new[] { gamesPath });
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
        var cache = new GameCache(new[] { gamesPath });
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

        var cache = new GameCache(new[] { path1, path2 });
        cache.ScanAll();

        Assert.True(cache.LookupCached("11111") != null);
        Assert.True(cache.LookupCached("22222") != null);
        Assert.Equal(2, cache.GetAll().Count);
    }

    // --- generate_emu_config placement: steam_appid.txt inside steam_settings/ ---

    [Fact]
    public void ScanAll_AppIdInsideSteamSettings_CachesGameRoot()
    {
        var achievementsJson = """[{"name": "ACH01", "displayName": "Test"}]""";
        var gameDir = Path.Combine(_tempDir, "games", "EmuConfigGame");
        var settingsDir = Path.Combine(gameDir, "steam_settings");
        Directory.CreateDirectory(settingsDir);
        File.WriteAllText(Path.Combine(settingsDir, "steam_appid.txt"), "77777");
        File.WriteAllText(Path.Combine(settingsDir, "achievements.json"), achievementsJson);

        var gamesPath = Path.Combine(_tempDir, "games");
        var cache = new GameCache(new[] { gamesPath });
        cache.ScanAll();

        var info = cache.Lookup("77777");
        Assert.NotNull(info);
        Assert.Equal("EmuConfigGame", info!.GameName);
        Assert.Equal(Path.Combine(settingsDir, "achievements.json"), info.MetadataPath);
    }

    [Fact]
    public void ScanAll_AppIdInBothLocations_SingleCacheEntry()
    {
        var achievementsJson = """[{"name": "ACH01", "displayName": "Test"}]""";
        var gameDir = Path.Combine(_tempDir, "games", "BothGame");
        var settingsDir = Path.Combine(gameDir, "steam_settings");
        Directory.CreateDirectory(settingsDir);
        File.WriteAllText(Path.Combine(gameDir, "steam_appid.txt"), "88888");
        File.WriteAllText(Path.Combine(settingsDir, "steam_appid.txt"), "88888");
        File.WriteAllText(Path.Combine(settingsDir, "achievements.json"), achievementsJson);

        var gamesPath = Path.Combine(_tempDir, "games");
        var cache = new GameCache(new[] { gamesPath });
        cache.ScanAll();

        Assert.True(cache.LookupCached("88888") != null);
        Assert.Single(cache.GetAll());
        var info = cache.Lookup("88888");
        Assert.NotNull(info);
        Assert.Equal(Path.Combine(settingsDir, "achievements.json"), info!.MetadataPath);
    }

    // --- GameName: derived from first-level subfolder of basePath ---

    [Fact]
    public void ScanAll_DeeplyNestedAppId_GameNameIsFirstLevelSubfolder()
    {
        // Mirrors the Aphelion case: steam_appid.txt is buried deep under the game root,
        // but GameName should be the first-level folder under the configured games path.
        var achievementsJson = """[{"name": "ACH01", "displayName": "Test"}]""";
        var gamesPath = Path.Combine(_tempDir, "games");
        var deepDir = Path.Combine(gamesPath, "Aphelion", "Aphelion", "Engine", "Binaries", "ThirdParty", "Steamworks", "Steamv157", "Win64");
        var settingsDir = Path.Combine(deepDir, "steam_settings");
        Directory.CreateDirectory(settingsDir);
        File.WriteAllText(Path.Combine(settingsDir, "steam_appid.txt"), "1966410");
        File.WriteAllText(Path.Combine(settingsDir, "achievements.json"), achievementsJson);

        var cache = new GameCache(new[] { gamesPath });
        cache.ScanAll();

        var info = cache.Lookup("1966410");
        Assert.NotNull(info);
        Assert.Equal("Aphelion", info!.GameName);
    }

    // --- Overlapping game paths ---

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScanAll_NestedRoots_NameTheGameAfterItsOwnFolder(bool innerFirst)
    {
        // The deepest configured root names the game, whichever order the two are listed in; the outer
        // one, which is the one walked, would name it "games".
        CreateSettingsDir("1966410", "Aphelion", "Engine", "Win64");
        var outer = _tempDir;
        var inner = Path.Combine(_tempDir, "games");

        var cache = new GameCache(innerFirst ? new[] { inner, outer } : new[] { outer, inner });
        cache.ScanAll();

        Assert.Equal("Aphelion", cache.LookupCached("1966410")?.GameName);
    }

    [Fact]
    public void ScanAll_OverlappingRoots_WalkEachFolderOnce()
    {
        var settings = CreateSettingsDir("1966410", "Aphelion");
        var games = Path.Combine(_tempDir, "games");
        var cache = new GameCache(new[] { games, _tempDir, games + @"\", games.ToUpperInvariant() });

        AppLog.While(cache.ScanAll);

        var lines = LogLines();
        Assert.Single(lines, line => line.Contains("Cached: appid=1966410", StringComparison.Ordinal));
        // One line per entry left unwalked, naming the entry that covers it, so a report shows why.
        var covered = lines.Where(line => line.Contains("is covered by", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, covered.Count);
        Assert.All(covered, line => Assert.Contains($"covered by '{_tempDir}'", line, StringComparison.Ordinal));
        Assert.Equal(Path.Combine(settings, "achievements.json"), cache.LookupCached("1966410")?.MetadataPath);
    }

    [Fact]
    public void ScanAll_GameAtAConfiguredRoot_IsNamedAfterThatFolder()
    {
        // The root itself leaves no folder below it to take a name from.
        CreateSettingsDir("1966410", "Aphelion");
        var game = Path.Combine(_tempDir, "games", "Aphelion");

        var cache = new GameCache(new[] { game });
        cache.ScanAll();

        Assert.Equal("Aphelion", cache.LookupCached("1966410")?.GameName);
    }

    [Fact]
    public void ScanAll_RootThatIsTheGamesOwnSettingsFolder_IsNamedAfterTheGameFolder()
    {
        // The game is the folder above that root, so no configured root holds it.
        var settings = CreateSettingsDir("1966410", "Aphelion");

        var cache = new GameCache(new[] { settings });
        cache.ScanAll();

        var info = cache.LookupCached("1966410");
        Assert.Equal("Aphelion", info?.GameName);
        Assert.Equal(new[] { settings }, info?.SettingsDirs);
    }

    [Fact]
    public void ScanAll_CoveredEntryWithATrailingSeparator_IsKeptInAReport()
    {
        // A report reads 'D:\Games\' as a folder inside its configured root 'D:\Games' and would drop the line.
        var gamesPaths = new[] { _tempDir, Path.Combine(_tempDir, "games") + @"\" };

        AppLog.While(new GameCache(gamesPaths).ScanAll);

        var covered = LogLines().Single(line => line.Contains("is covered by", StringComparison.Ordinal));
        var inputs = DiagnosticReport.Collect("1966410", null, Array.Empty<string>(), gamesPaths);
        Assert.Single(DiagnosticReport.KeepLinesForGame(new[] { covered }, "1966410", inputs.ConfiguredRoots, inputs.GameFolders));
    }

    [Fact]
    public void Collect_ReadsTheStatsFileBesideTheSchema()
    {
        // GBE reads stats.json from the same steam_settings folder as the schema, and counts
        // achievement progress only against the stats it defines.
        var ss = Path.Combine(_tempDir, "games", "StatsGame", "steam_settings");
        Directory.CreateDirectory(ss);
        File.WriteAllText(Path.Combine(ss, "steam_appid.txt"), "44444");
        File.WriteAllText(Path.Combine(ss, "achievements.json"), """[{"name": "ACH01"}]""");
        File.WriteAllText(Path.Combine(ss, "stats.json"), """[{"name": "kills", "type": "int", "default": "0"}]""");
        var cache = new GameCache(new[] { Path.Combine(_tempDir, "games") });
        cache.ScanAll();

        var inputs = DiagnosticReport.Collect("44444", cache.LookupCached("44444"), Array.Empty<string>(), Array.Empty<string>());

        Assert.Equal("ok", inputs.Stats.Status);
        Assert.Contains("kills", inputs.Stats.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Collect_NoStatsFile_IsReportedMissing()
    {
        var ss = Path.Combine(_tempDir, "games", "NoStatsGame", "steam_settings");
        Directory.CreateDirectory(ss);
        File.WriteAllText(Path.Combine(ss, "steam_appid.txt"), "55555");
        File.WriteAllText(Path.Combine(ss, "achievements.json"), """[{"name": "ACH01"}]""");
        var cache = new GameCache(new[] { Path.Combine(_tempDir, "games") });
        cache.ScanAll();

        var inputs = DiagnosticReport.Collect("55555", cache.LookupCached("55555"), Array.Empty<string>(), Array.Empty<string>());

        Assert.Equal("missing", inputs.Stats.Status);
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
        var cache = new GameCache(new[] { gamesPath });
        cache.ScanAll();

        Assert.True(cache.LookupCached("33333") != null);
    }

    // --- Games carrying more than one steam_settings folder ---

    private const string Schema = """[{"name": "ACH01", "displayName": "Test"}]""";

    /// <summary>Writes a steam_settings folder with its own appid file and schema, and returns it.</summary>
    private string CreateSettingsDir(string appId, params string[] relativeParts)
    {
        var dir = Path.Combine(new[] { _tempDir, "games" }.Concat(relativeParts).Append("steam_settings").ToArray());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "steam_appid.txt"), appId);
        File.WriteAllText(Path.Combine(dir, "achievements.json"), Schema);
        return dir;
    }

    [Fact]
    public void ScanAll_GameWithTwoSettingsFolders_KeepsBothDeepestFirst()
    {
        // A repack's decorated copy at the game root, plus the one the emulator actually reads.
        var root = CreateSettingsDir("2378900", "Coffin");
        var nested = CreateSettingsDir("2378900", "Coffin", "Coffin", "www", "greenworks", "lib");

        var cache = new GameCache(new[] { Path.Combine(_tempDir, "games") });
        cache.ScanAll();

        var info = cache.LookupCached("2378900")!;
        Assert.Equal(new[] { nested, root }, info.SettingsDirs);
        // The deepest is the schema source, so icons and achievement text are unaffected.
        Assert.Equal(Path.Combine(nested, "achievements.json"), info.MetadataPath);
        Assert.Single(cache.GetAll());
    }

    [Fact]
    public void ScanAll_TwoGamesClaimingOneAppId_DoNotPoolTheirFolders()
    {
        // Without the game-folder half of the grouping key, an appid collision would answer an
        // unlock with a mixture of two unrelated installs' settings.
        CreateSettingsDir("480", "GameA");
        CreateSettingsDir("480", "GameB");

        var cache = new GameCache(new[] { Path.Combine(_tempDir, "games") });
        cache.ScanAll();

        Assert.Single(cache.LookupCached("480")!.SettingsDirs);
    }

    [Fact]
    public void ScanAll_InstallsNamedAlikeUnderNestedRoots_DoNotPoolTheirFolders()
    {
        // Each install is named "X" below its own deepest root, so only the folder tells them apart.
        CreateSettingsDir("444", "X");
        CreateSettingsDir("444", "Games", "X");
        var root = Path.Combine(_tempDir, "games");

        var cache = new GameCache(new[] { root, Path.Combine(root, "Games") });
        cache.ScanAll();

        var info = cache.LookupCached("444")!;
        Assert.Equal("X", info.GameName);
        Assert.Single(info.SettingsDirs);
    }

    [Fact]
    public void ScanAll_AppIdFileAtTheGameRootAndInSettings_IsOneFolder()
    {
        // The Red Dead shape: both files name the same steam_settings folder.
        var settings = CreateSettingsDir("2668510", "RDR");
        File.WriteAllText(Path.Combine(_tempDir, "games", "RDR", "steam_appid.txt"), "2668510");

        var cache = new GameCache(new[] { Path.Combine(_tempDir, "games") });
        cache.ScanAll();

        Assert.Equal(new[] { settings }, cache.LookupCached("2668510")!.SettingsDirs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScanAll_RootThatIsTheGamesOwnFolder_KeepsEveryCopyAsOneGame(bool appIdInSettings)
    {
        // A repack at a root picked by hand: the appid file beside the exe or in the settings folder, a
        // decorated copy at the top, and the copy the emulator reads beside its DLL. Named by its first
        // folder below the root, that copy would be a second game called "bin" holding only its own folder.
        var game = Path.Combine(_tempDir, "games", "A");
        var top = Path.Combine(game, "steam_settings");
        Directory.CreateDirectory(top);
        File.WriteAllText(Path.Combine(top, "achievements.json"), Schema);
        File.WriteAllText(Path.Combine(appIdInSettings ? top : game, "steam_appid.txt"), "480");
        var nested = CreateSettingsDir("480", "A", "bin", "coldclient");

        var cache = new GameCache(new[] { game });
        cache.ScanAll();

        var info = cache.LookupCached("480")!;
        Assert.Equal("A", info.GameName);
        Assert.Equal(new[] { nested, top }, info.SettingsDirs);
        // The Settings window's folder card counts with the same grouping.
        Assert.Single(GameCache.FindGames(game, new[] { FolderPath.Parse(game) }).Games);
    }

    [Fact]
    public void ScanAll_RootHoldingOnlyTheAppIdFile_NamesTheGameAfterTheRoot()
    {
        // An appid file with no schema beside it still marks the root as the game.
        var game = Path.Combine(_tempDir, "games", "A");
        var nested = CreateSettingsDir("480", "A", "bin", "coldclient");
        File.WriteAllText(Path.Combine(game, "steam_appid.txt"), "480");

        var cache = new GameCache(new[] { game });
        cache.ScanAll();

        var info = cache.LookupCached("480")!;
        Assert.Equal("A", info.GameName);
        Assert.Equal(new[] { nested }, info.SettingsDirs);
    }

    [Fact]
    public void ScanAll_GameFolderRootInsideAnotherRoot_ClaimsItsCopiesUnderTheFoldersOwnSpelling()
    {
        // The claiming root is reached by the outer walk and spelled differently in config: its copies
        // still join, and the game keeps the folder's spelling on disk.
        var games = Path.Combine(_tempDir, "games");
        var top = CreateSettingsDir("480", "Aphelion");
        var nested = CreateSettingsDir("480", "Aphelion", "bin", "coldclient");

        var cache = new GameCache(new[] { games, Path.Combine(games, "APHELION") + @"\" });
        cache.ScanAll();

        var info = cache.LookupCached("480")!;
        Assert.Equal("Aphelion", info.GameName);
        Assert.Equal(new[] { nested, top }, info.SettingsDirs);
    }

    [Fact]
    public void ScanAll_RootHoldingAnotherAppId_DoesNotClaimTheGamesBelowIt()
    {
        // Only the same appid makes a root the game's own folder.
        CreateSettingsDir("480", "A");
        CreateSettingsDir("999", "A", "Tools", "bin");
        var game = Path.Combine(_tempDir, "games", "A");

        var cache = new GameCache(new[] { game });
        cache.ScanAll();

        Assert.Equal("A", cache.LookupCached("480")?.GameName);
        Assert.Equal("Tools", cache.LookupCached("999")?.GameName);
    }

    [Fact]
    public void FindGames_CountsEachGameOnceAndReportsWhatItSkipped()
    {
        // Five steam_appid.txt files, two games: what a count of the files alone got wrong in Settings.
        CreateSettingsDir("2378900", "Coffin");
        CreateSettingsDir("2378900", "Coffin", "Coffin", "www", "greenworks", "lib");
        CreateSettingsDir("2668510", "RDR");
        File.WriteAllText(Path.Combine(_tempDir, "games", "RDR", "steam_appid.txt"), "2668510");
        CreateGameDir("NoSchema", "11111");
        var root = FolderPath.Parse(Path.Combine(_tempDir, "games"));

        var (games, skipped) = GameCache.FindGames(root.ToString(), new[] { root });

        Assert.Equal(new[] { "Coffin", "RDR" }, games.Select(game => game.GameName).Order(StringComparer.Ordinal));
        Assert.Contains("appid=11111", Assert.Single(skipped), StringComparison.Ordinal);
    }

    [Fact]
    public void FindGames_RootThatCannotBeWalked_Throws()
    {
        var missing = FolderPath.Parse(Path.Combine(_tempDir, "missing"));

        Assert.Throws<DirectoryNotFoundException>(() => GameCache.FindGames(missing.ToString(), new[] { missing }));
    }

    [Fact]
    public void ScanAll_HiddenSettingsFolder_IsStillFound()
    {
        // Repacks hide steam_settings routinely; the default enumeration skips hidden entries, which
        // made such a game invisible with nothing in the log to say so.
        var settings = CreateSettingsDir("44444", "HiddenGame");
        File.SetAttributes(settings, File.GetAttributes(settings) | FileAttributes.Hidden);

        var cache = new GameCache(new[] { Path.Combine(_tempDir, "games") });
        cache.ScanAll();

        Assert.Equal(new[] { settings }, cache.LookupCached("44444")?.SettingsDirs);
    }
}
