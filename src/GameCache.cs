using System.Collections.Concurrent;
using System.IO;

namespace AchievementOverlay;

/// <summary>
/// Cached game info: maps an appid to the directory containing steam_settings/achievements.json.
/// </summary>
public sealed class GameInfo
{
    public required string AppId { get; init; }
    public required string MetadataPath { get; init; }
    public required string GameName { get; init; }

    /// <summary>
    /// Every <c>steam_settings</c> folder this game has, deepest first. One install often carries
    /// more than one — a repack drops a decorated copy at the game root while the emulator reads the
    /// one beside the DLL (<c>bin/coldclient/</c>, <c>www/greenworks/lib/</c>) — and they rarely hold
    /// the same things. The first is <see cref="MetadataPath"/>'s folder; the rest are consulted only
    /// for a game's own overlay settings, where a folder that GBE itself never reads can still be the
    /// only record of the sound and font the user chose.
    /// </summary>
    public required IReadOnlyList<string> SettingsDirs { get; init; }
}

/// <summary>
/// Scans configured game paths for steam_appid.txt files (in either the game root
/// or inside steam_settings/), reads appids, and caches the mapping from appid to
/// achievement metadata path.
/// </summary>
public sealed class GameCache
{
    // Compares a scan's grouping key: the appid exactly, and the folder without case, as Windows compares folders.
    private static readonly IEqualityComparer<(string AppId, string GameFolder)> SameGame = EqualityComparer<(string AppId, string GameFolder)>.Create((x, y) => x.AppId == y.AppId && string.Equals(x.GameFolder, y.GameFolder, StringComparison.OrdinalIgnoreCase), key => HashCode.Combine(key.AppId, StringComparer.OrdinalIgnoreCase.GetHashCode(key.GameFolder)));

    private readonly ConcurrentDictionary<string, GameInfo> _cache = new();

    // Appids a LookupScanningOnce miss has already spent a rescan on. Concurrent because unlocks are
    // resolved on fire-and-forget watcher tasks.
    private readonly ConcurrentDictionary<string, byte> _rescannedAppIds = new();

    private readonly AppConfig? _config;
    private readonly IReadOnlyList<string>? _staticGamesPaths;

    public GameCache(AppConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Constructor for testing — accepts static paths instead of AppConfig.
    /// </summary>
    internal GameCache(IReadOnlyList<string> gamesPaths)
    {
        _staticGamesPaths = gamesPaths;
    }

    private IReadOnlyList<string> GetGamesPaths() => _config?.GamesPaths ?? _staticGamesPaths ?? Array.Empty<string>();

    /// <summary>
    /// Scans every configured game path. Each folder is walked once: an entry inside another, or
    /// naming the same folder again, is scanned as part of that one — config is left as written.
    /// </summary>
    public void ScanAll()
    {
        Logger.Info("Starting game cache scan...");
        var count = 0;

        var configured = GetGamesPaths().Select(path => (Path: path, Folder: FolderPath.Parse(path))).ToList();
        var roots = configured.Select(entry => entry.Folder).ToList();
        var survivors = FolderPath.Minimal(roots);
        var scanned = configured.Where(entry => survivors.Any(survivor => ReferenceEquals(survivor, entry.Folder))).ToList();

        // Said in the log, so a report shows why a configured folder has no scan of its own.
        foreach (var (path, folder) in configured)
        {
            var covering = scanned.First(entry => entry.Folder.Contains(folder));
            if (!ReferenceEquals(covering.Folder, folder))
                Logger.Info($"  Game path '{AsReported(path)}' is covered by '{AsReported(covering.Path)}', so it is not scanned separately");
        }

        foreach (var (basePath, _) in scanned)
        {
            if (!Directory.Exists(basePath))
            {
                Logger.Warn($"  Game path does not exist, skipping: '{AsReported(basePath)}'");
                continue;
            }

            count += ScanDirectory(basePath, roots);
        }

        Logger.Info($"Game cache scan complete. Found {count} game(s) with achievement metadata:");

        // A configured path as a diagnostic report spells its configured roots: without a trailing
        // separator. The report reads 'D:\Games\' as a folder inside the root 'D:\Games' and drops the line.
        static string AsReported(string path) => Path.TrimEndingDirectorySeparator(path);
    }

    public IEnumerable<string> GetAllAppIds() => _cache.Keys;

    /// <summary>
    /// Looks up a game by appid without rescanning. Use this on paths that run per achievement or
    /// per GSE Saves folder, where a rescan miss would be paid over and over.
    /// </summary>
    public GameInfo? LookupCached(string appId) => _cache.TryGetValue(appId, out var info) ? info : null;

    /// <summary>
    /// Looks up a game by appid, rescanning at most once per appid. Use this where a miss is a normal
    /// steady state — a game tracked through a self-describing unlock file needs no steam_settings/ at
    /// all, and an unthrottled <see cref="Lookup"/> would walk every configured games path again on
    /// every unlock. The one attempt still picks up a config added after the last scan.
    /// </summary>
    public GameInfo? LookupScanningOnce(string appId)
    {
        if (_cache.TryGetValue(appId, out var info))
            return info;

        return _rescannedAppIds.TryAdd(appId, 0) ? Lookup(appId) : null;
    }

    /// <summary>
    /// Looks up a game by appid. If not found, triggers a re-scan and tries again.
    /// </summary>
    public GameInfo? Lookup(string appId)
    {
        if (_cache.TryGetValue(appId, out var info))
            return info;

        // Cache miss — re-scan to pick up newly installed games
        Logger.Info($"Cache miss for appid {appId}, re-scanning...");
        ScanAll();

        _cache.TryGetValue(appId, out info);
        return info;
    }

    /// <summary>
    /// Gets all cached game entries (for diagnostics/logging).
    /// </summary>
    public IReadOnlyCollection<GameInfo> GetAll() => _cache.Values.ToList().AsReadOnly();

    /// <summary>
    /// Scans one root. <paramref name="roots"/> is every configured root, the ones scanned as part of
    /// this one included, because a game is named after its folder below the deepest of them.
    /// </summary>
    private int ScanDirectory(string basePath, IReadOnlyList<FolderPath> roots)
    {
        IReadOnlyList<GameInfo> games;
        IReadOnlyList<string> skipped;
        try
        {
            (games, skipped) = FindGames(basePath, roots);
        }
#pragma warning disable CA1031 // Per-root boundary: a hand-edited gamesPaths entry can fail in many ways; logs at Warn and scans the other roots
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Logger.Warn($"  Error scanning '{basePath}': {ex.Message}");
            return 0;
        }

        foreach (var line in skipped)
            Logger.Warn($"  {line}");

        foreach (var game in games)
        {
            _cache[game.AppId] = game;

            var extra = game.SettingsDirs.Count > 1 ? $" (+{game.SettingsDirs.Count - 1} more settings folder(s): {string.Join(", ", game.SettingsDirs.Skip(1).Select(d => $"'{d}'"))})" : "";
            Logger.Info($"  Cached: appid={game.AppId}, game={game.GameName}, path='{game.MetadataPath}'{extra}");
            // Carries the appid so the line identifies its own game: a diagnostic report keeps the
            // lines about one game and drops the rest, and it recognises them by the appid.
            Logger.Info($"  Schema: appid={game.AppId}, {DescribeSchema(game)}");
        }

        return games.Count;
    }

    /// <summary>
    /// The games under one root, one per appid and game folder, and a line for each find that was
    /// skipped and why. <paramref name="roots"/> is every configured root, as for
    /// <see cref="ScanDirectory"/>. The Settings window counts a games folder with this too, so its
    /// card and the cache cannot disagree about what a game is. Throws when the root cannot be walked.
    /// </summary>
    public static (IReadOnlyList<GameInfo> Games, IReadOnlyList<string> Skipped) FindGames(string basePath, IReadOnlyList<FolderPath> roots)
    {
        // The game folder of every readable steam_appid.txt, whether or not a schema sits beside it, and
        // apart from them the finds that do have a schema. Grouping waits for the whole walk: a root
        // among those folders claims the copies of its appid below it (NameGame), and the walk can meet
        // a nested copy before the root's own file.
        var appIdFolders = new List<(string AppId, FolderPath Folder)>();
        var finds = new List<(string AppId, FolderPath GameDir, string SettingsDir)>();
        var skipped = new List<string>();

        foreach (var appIdFile in Directory.EnumerateFiles(basePath, "steam_appid.txt", AppUtilities.RecursiveScan))
        {
            try
            {
                var appId = ReadAppId(appIdFile);
                if (string.IsNullOrWhiteSpace(appId))
                {
                    // Skipping is right; doing it silently is not. An empty, whitespace-only or
                    // UTF-16 steam_appid.txt reads as blank here, and the game then goes untracked
                    // with nothing anywhere to say why — the shape of report issue #2 was closed on.
                    skipped.Add($"Skipped: '{appIdFile}' holds no readable appid (empty, or not UTF-8/ASCII text)");
                    continue;
                }

                var gameDir = Path.GetDirectoryName(appIdFile)!;
                // generate_emu_config places steam_appid.txt inside steam_settings/ — collapse to game root
                if (string.Equals(Path.GetFileName(gameDir), "steam_settings", StringComparison.OrdinalIgnoreCase))
                    gameDir = Path.GetDirectoryName(gameDir)!;
                var folder = FolderPath.Parse(gameDir);
                appIdFolders.Add((appId, folder));
                var settingsDir = Path.Combine(gameDir, "steam_settings");

                if (!File.Exists(Path.Combine(settingsDir, "achievements.json")))
                {
                    skipped.Add($"Skipped: appid={appId} at '{gameDir}' (no 'achievements.json')");
                    continue;
                }

                finds.Add((appId, folder, settingsDir));
            }
#pragma warning disable CA1031 // Per-game boundary: returns the failure among the skipped finds, which the scan logs at Warn, and goes on to the other games
            catch (Exception ex)
#pragma warning restore CA1031
            {
                skipped.Add($"Error processing '{appIdFile}': {ex.Message}");
            }
        }

        // Keyed by appid *and* first-level game folder, not appid alone: two installs claiming one appid
        // are two games, and folding their folders together would answer an unlock with a mixture of
        // both. By the folder rather than the name it gives the game, because each install is named
        // below its own deepest root, so installs under two nested roots can share a name.
        var byGame = new Dictionary<(string AppId, string GameFolder), (string GameName, List<string> Dirs)>(SameGame);
        var foldersByAppId = appIdFolders.ToLookup(entry => entry.AppId, entry => entry.Folder);

        foreach (var (appId, gameDir, settingsDir) in finds)
        {
            var (gameFolder, gameName) = NameGame(gameDir, roots, foldersByAppId[appId]);
            if (!byGame.TryGetValue((appId, gameFolder), out var game))
                byGame[(appId, gameFolder)] = game = (gameName, new List<string>());
            // A steam_appid.txt at the game root and one inside steam_settings/ name the same folder.
            if (!game.Dirs.Contains(settingsDir, StringComparer.OrdinalIgnoreCase))
                game.Dirs.Add(settingsDir);
        }

        var games = byGame.Select(entry =>
        {
            // Deepest first: the emulator loads from beside its DLL, which is the nested copy in every
            // layout seen so far (bin/coldclient, www/greenworks/lib, Binaries/Win64). Ordering by
            // path keeps ties stable, so which folder supplies the schema stops depending on the order
            // the filesystem happened to enumerate in.
            var ordered = entry.Value.Dirs
                .OrderByDescending(d => d.Count(c => c is '\\' or '/'))
                .ThenBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new GameInfo
            {
                AppId = entry.Key.AppId,
                MetadataPath = Path.Combine(ordered[0], "achievements.json"),
                GameName = entry.Value.GameName,
                SettingsDirs = ordered
            };
        }).ToList();

        return (games, skipped);
    }

    /// <summary>
    /// A one-line digest of a game's schema: how many achievements, how their names are spelled, and
    /// which languages the text carries. These are the questions every report about a wrong name,
    /// a missing icon or an ignored language turns on, and answering them in the log means a reporter
    /// no longer has to open the file and describe it by hand.
    /// </summary>
    private static string DescribeSchema(GameInfo game)
    {
        var definitions = LoadDefinitions(game);
        if (definitions == null)
            return "could not be read";

        var languages = AchievementMetadata.CollectLanguages(definitions);
        var shape = AchievementMetadata.DescribeTextShape(definitions.Select(d => d.DisplayName));
        var names = AchievementMetadata.DescribeNameStyle(definitions.Select(d => d.Name));
        var langs = languages.Count > 0 ? string.Join(",", languages.OrderBy(l => l, StringComparer.OrdinalIgnoreCase)) : "-";
        return $"count={definitions.Count}, names={names}, text={shape}, langs=[{langs}]";
    }

    /// <summary>
    /// A game's first-level folder, which it is grouped by, and its name, which is that folder's: the
    /// first folder below the deepest configured root holding it. So with both <c>D:\</c> and
    /// <c>D:\Games</c> configured, <c>D:\Games\Aphelion\...\Win64</c> is <c>D:\Games\Aphelion</c>,
    /// "Aphelion", rather than <c>D:\Games</c>, "Games". A game sitting at a root is that folder, and so
    /// is a game no root holds: one found under a root that is its own <c>steam_settings</c> folder,
    /// which leaves the game the folder above it.
    /// </summary>
    /// <remarks>
    /// A root that is itself among <paramref name="sameAppIdFolders"/> — the game folders of every
    /// <c>steam_appid.txt</c> carrying this game's appid — is the game's own folder rather than a games
    /// root, so every copy below it that no deeper configured root holds is that folder too. With
    /// <c>D:\Games\A</c> configured, a repack's <c>D:\Games\A\bin\coldclient</c> copy is
    /// <c>D:\Games\A</c>, "A", and joins the copy at the top, rather than splitting off as a second game
    /// called "bin". The folder is taken from the walk's record of it rather than from the root, so the
    /// name is spelled as the scan found the folder, as every other game's is.
    /// </remarks>
    private static (string Folder, string Name) NameGame(FolderPath game, IReadOnlyList<FolderPath> roots, IEnumerable<FolderPath> sameAppIdFolders)
    {
        var anchor = roots.Where(root => root.Contains(game)).MaxBy(root => root.Names.Count) ?? game;
        var named = sameAppIdFolders.FirstOrDefault(anchor.IsSameFolder) ?? game;
        var name = named.FirstNameBelow(anchor);
        return (anchor.Names.Count < named.Names.Count ? Path.Join(anchor.ToString(), name) : named.ToString(), name);
    }

    private static string ReadAppId(string appIdFilePath)
    {
        var content = File.ReadAllText(appIdFilePath).Trim();
        // steam_appid.txt contains just the numeric appid
        return content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                      .FirstOrDefault() ?? "";
    }

    /// <summary>
    /// Loads and parses the achievement definitions for a given game.
    /// </summary>
    public static List<AchievementDefinition>? LoadDefinitions(GameInfo gameInfo)
    {
        try
        {
            var json = File.ReadAllText(gameInfo.MetadataPath);
            return AchievementMetadata.ParseDefinitions(json);
        }
#pragma warning disable CA1031 // Per-schema boundary: logs the unreadable file at Warn and returns null, which callers read as no schema
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Logger.Warn($"Failed to load achievement definitions for appid {gameInfo.AppId} from '{gameInfo.MetadataPath}': {ex.Message}");
            return null;
        }
    }
}
