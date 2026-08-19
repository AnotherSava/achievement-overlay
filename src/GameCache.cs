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
    private readonly ConcurrentDictionary<string, GameInfo> _cache = new();

    // Appids a LookupScanningOnce miss has already spent a rescan on. Concurrent because unlocks are
    // resolved on fire-and-forget watcher tasks.
    private readonly ConcurrentDictionary<string, byte> _rescannedAppIds = new();

    private readonly AppConfig? _config;
    private readonly string[]? _staticGamesPaths;

    public GameCache(AppConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Constructor for testing — accepts static paths instead of AppConfig.
    /// </summary>
    internal GameCache(string[] gamesPaths)
    {
        _staticGamesPaths = gamesPaths;
    }

    private string[] GetGamesPaths() => _config?.GamesPaths ?? _staticGamesPaths ?? Array.Empty<string>();

    /// <summary>
    /// Performs initial scan of all configured game paths.
    /// </summary>
    public void ScanAll()
    {
        Logger.Info("Starting game cache scan...");
        var count = 0;

        foreach (var basePath in GetGamesPaths())
        {
            if (!Directory.Exists(basePath))
            {
                Logger.Warn($"  Game path does not exist, skipping: '{basePath}'");
                continue;
            }

            count += ScanDirectory(basePath);
        }

        Logger.Info($"Game cache scan complete. Found {count} game(s) with achievement metadata:");
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

    private int ScanDirectory(string basePath)
    {
        IEnumerable<string> appIdFiles;
        try
        {
            appIdFiles = Directory.EnumerateFiles(basePath, "steam_appid.txt", AppUtilities.RecursiveScan);
        }
        catch (Exception ex)
        {
            Logger.Info($"  Error scanning '{basePath}': {ex.Message}");
            return 0;
        }

        // Keyed by appid *and* game folder, not appid alone: two installs claiming one appid are two
        // games, and folding their folders together would answer an unlock with a mixture of both.
        var byGame = new Dictionary<(string AppId, string GameName), List<string>>();

        foreach (var appIdFile in appIdFiles)
        {
            try
            {
                var appId = ReadAppId(appIdFile);
                if (string.IsNullOrWhiteSpace(appId))
                    continue;

                var gameDir = Path.GetDirectoryName(appIdFile)!;
                // generate_emu_config places steam_appid.txt inside steam_settings/ — collapse to game root
                if (string.Equals(Path.GetFileName(gameDir), "steam_settings", StringComparison.OrdinalIgnoreCase))
                    gameDir = Path.GetDirectoryName(gameDir)!;
                var settingsDir = Path.Combine(gameDir, "steam_settings");

                if (!File.Exists(Path.Combine(settingsDir, "achievements.json")))
                {
                    Logger.Warn($"  Skipped: appid={appId} at '{gameDir}' (no 'achievements.json')");
                    continue;
                }

                var key = (appId, ExtractGameName(basePath, gameDir));
                if (!byGame.TryGetValue(key, out var dirs))
                    byGame[key] = dirs = new List<string>();
                // A steam_appid.txt at the game root and one inside steam_settings/ name the same folder.
                if (!dirs.Contains(settingsDir, StringComparer.OrdinalIgnoreCase))
                    dirs.Add(settingsDir);
            }
            catch (Exception ex)
            {
                Logger.Info($"  Error processing '{appIdFile}': {ex.Message}");
            }
        }

        foreach (var ((appId, gameName), dirs) in byGame)
        {
            // Deepest first: the emulator loads from beside its DLL, which is the nested copy in every
            // layout seen so far (bin/coldclient, www/greenworks/lib, Binaries/Win64). Ordering by
            // path keeps ties stable, so which folder supplies the schema stops depending on the order
            // the filesystem happened to enumerate in.
            var ordered = dirs
                .OrderByDescending(d => d.Count(c => c is '\\' or '/'))
                .ThenBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _cache[appId] = new GameInfo
            {
                AppId = appId,
                MetadataPath = Path.Combine(ordered[0], "achievements.json"),
                GameName = gameName,
                SettingsDirs = ordered
            };

            var extra = ordered.Count > 1 ? $" (+{ordered.Count - 1} more settings folder(s): {string.Join(", ", ordered.Skip(1).Select(d => $"'{d}'"))})" : "";
            Logger.Info($"  Cached: appid={appId}, game={gameName}, path='{ordered[0]}\\achievements.json'{extra}");
        }

        return byGame.Count;
    }

    /// <summary>
    /// Extracts the first-level subfolder name of <paramref name="gameDir"/> relative to <paramref name="basePath"/>.
    /// E.g. basePath=C:\Games, gameDir=C:\Games\Aphelion\...\Win64 → "Aphelion".
    /// </summary>
    private static string ExtractGameName(string basePath, string gameDir)
    {
        var baseFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(basePath));
        var gameFull = Path.GetFullPath(gameDir);
        var relative = Path.GetRelativePath(baseFull, gameFull);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
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
        catch (Exception ex)
        {
            Logger.Warn($"Failed to load achievement definitions for appid {gameInfo.AppId} from '{gameInfo.MetadataPath}': {ex.Message}");
            return null;
        }
    }
}
