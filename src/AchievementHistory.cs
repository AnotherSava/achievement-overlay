using System.IO;

namespace AchievementOverlay;

public sealed class AchievementHistoryEntry
{
    public required string AppId { get; init; }
    public required string GameName { get; init; }
    public required string AchievementName { get; init; }
    public required string Description { get; init; }
    public string? IconPath { get; init; }
    public required long EarnedTime { get; init; }
}

/// <summary>
/// Queries GSE Saves for recently earned achievements across all tracked games.
/// </summary>
public sealed class AchievementHistory
{
    private readonly AppConfig _config;
    private readonly GameCache _gameCache;

    public AchievementHistory(AppConfig config, GameCache gameCache)
    {
        _config = config;
        _gameCache = gameCache;
    }

    /// <summary>
    /// Returns the most recent earned achievements, sorted by earned_time descending.
    /// Only includes achievements for tracked games (present in GameCache).
    /// Always includes a synthetic "Achievement Overlay installed" entry.
    /// </summary>
    public List<AchievementHistoryEntry> GetRecent(int count)
    {
        var entries = new List<AchievementHistoryEntry>();

        foreach (var gseSavesPath in _config.GseSavesPaths)
        {
            if (!Directory.Exists(gseSavesPath))
                continue;

            foreach (var dir in Directory.GetDirectories(gseSavesPath))
            {
                var appId = Path.GetFileName(dir);
                var gameInfo = _gameCache.Contains(appId) ? _gameCache.Lookup(appId) : null;
                if (gameInfo == null)
                    continue;

                var achievementsFile = Path.Combine(dir, "achievements.json");
                if (!File.Exists(achievementsFile))
                    continue;

                try
                {
                    var json = File.ReadAllText(achievementsFile);
                    var states = AchievementMetadata.ParseUnlockStates(json);

                    foreach (var (achName, state) in states)
                    {
                        if (!state.Earned)
                            continue;

                        var resolved = AchievementMetadata.Resolve(_gameCache, appId, achName, _config.Language);
                        entries.Add(new AchievementHistoryEntry
                        {
                            AppId = appId,
                            GameName = gameInfo.GameName,
                            AchievementName = resolved?.DisplayName ?? achName,
                            Description = resolved?.Description ?? "",
                            IconPath = resolved?.IconPath,
                            EarnedTime = state.EarnedTime
                        });
                    }
                }
                catch (Exception ex)
                {
                    Logger.Info($"Error reading achievements for appid {appId}: {ex.Message}");
                }
            }
        }

        // Synthetic entry with exe modified date
        var exePath = Environment.ProcessPath ?? typeof(AchievementHistory).Assembly.Location;
        var installedTime = File.Exists(exePath) ? new DateTimeOffset(File.GetLastWriteTimeUtc(exePath), TimeSpan.Zero).ToUnixTimeSeconds() : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        entries.Add(new AchievementHistoryEntry
        {
            AppId = "",
            GameName = "Achievement Overlay",
            AchievementName = "Achievement Connoisseur",
            Description = "Install and configure Achievement Overlay",
            IconPath = GetSyntheticIconPath(),
            EarnedTime = installedTime
        });

        return entries.OrderByDescending(e => e.EarnedTime).Take(count).ToList();
    }

    private static string? _syntheticIconPath;

    private static string? GetSyntheticIconPath()
    {
        if (_syntheticIconPath != null && File.Exists(_syntheticIconPath))
            return _syntheticIconPath;

        var stream = typeof(AchievementHistory).Assembly.GetManifestResourceStream("AchievementOverlay.connoisseur.jpg");
        if (stream == null)
            return null;

        var tempPath = Path.Combine(Path.GetTempPath(), "AchievementOverlay_connoisseur.jpg");
        using (var file = File.Create(tempPath))
            stream.CopyTo(file);

        _syntheticIconPath = tempPath;
        return tempPath;
    }
}
