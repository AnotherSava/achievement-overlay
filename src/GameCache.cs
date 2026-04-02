using System.Collections.Concurrent;
using System.IO;

namespace AchievementOverlay;

/// <summary>
/// Cached game info: maps an appid to the directory containing steam_settings/achievements.json.
/// </summary>
public sealed class GameInfo
{
    public required string AppId { get; init; }
    public required string GameDir { get; init; }
    public required string MetadataPath { get; init; }
    public string GameName => Path.GetFileName(GameDir);
}

/// <summary>
/// Scans configured game paths for steam_appid.txt files, reads appids,
/// and caches the mapping from appid to achievement metadata path.
/// </summary>
public sealed class GameCache
{
    private readonly ConcurrentDictionary<string, GameInfo> _cache = new();
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

        if (count > 0)
            Logger.Info($"Game cache scan complete. Found {count} game(s) with achievement metadata:");
        else
            Logger.Warn("Game cache scan complete. No games with achievement metadata found — check 'gamesPaths' in config");
    }

    public IEnumerable<string> GetAllAppIds() => _cache.Keys;

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
    /// Checks if an appid is in the cache without triggering a re-scan.
    /// </summary>
    public bool Contains(string appId) => _cache.ContainsKey(appId);

    private int ScanDirectory(string basePath)
    {
        var count = 0;

        IEnumerable<string> appIdFiles;
        try
        {
            appIdFiles = Directory.EnumerateFiles(basePath, "steam_appid.txt",
                new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true });
        }
        catch (Exception ex)
        {
            Logger.Info($"  Error scanning '{basePath}': {ex.Message}");
            return 0;
        }

        foreach (var appIdFile in appIdFiles)
        {
            try
            {
                var appId = ReadAppId(appIdFile);
                if (string.IsNullOrWhiteSpace(appId))
                    continue;

                var gameDir = Path.GetDirectoryName(appIdFile)!;
                var metadataPath = Path.Combine(gameDir, "steam_settings", "achievements.json");

                if (!File.Exists(metadataPath))
                {
                    Logger.Warn($"  Skipped: appid={appId} at '{gameDir}' (no 'achievements.json')");
                    continue;
                }

                var info = new GameInfo
                {
                    AppId = appId,
                    GameDir = gameDir,
                    MetadataPath = metadataPath
                };

                _cache[appId] = info;
                Logger.Info($"  Cached: appid={appId}, game={info.GameName}, path='{metadataPath}'");
                count++;
            }
            catch (Exception ex)
            {
                Logger.Info($"  Error processing '{appIdFile}': {ex.Message}");
            }
        }

        return count;
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
        catch
        {
            return null;
        }
    }
}
