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
        var trackingConfigured = _config.GetCurrent().TrackingConfigured ?? new Dictionary<string, long>();

        foreach (var gseSavesPath in _config.GseSavesPaths)
        {
            if (!Directory.Exists(gseSavesPath))
                continue;

            foreach (var dir in Directory.GetDirectories(gseSavesPath))
            {
                var appId = Path.GetFileName(dir);
                // LookupCached, never Lookup — a rescan per unknown folder would run on every
                // press of the Recent-achievements shortcut.
                var gameInfo = _gameCache.LookupCached(appId);

                Dictionary<string, AchievementUnlockState>? states = null;
                var achievementsFile = Path.Combine(dir, "achievements.json");
                if (File.Exists(achievementsFile))
                {
                    try
                    {
                        states = AchievementMetadata.ParseUnlockStates(File.ReadAllText(achievementsFile));
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Error reading achievements for appid {appId}: {ex.Message}");
                    }
                }

                // A game qualifies by being configured, or by describing itself in its unlock file.
                var selfDescribing = states != null && AchievementMetadata.IsSelfDescribing(states);
                if (gameInfo == null && !selfDescribing)
                    continue;

                var gameName = gameInfo?.GameName ?? appId;

                // Synthetic "tracking configured" milestone, timestamped by when the notification
                // first fired. Added even before any real unlock exists.
                if (trackingConfigured.TryGetValue(appId, out var configuredTime))
                {
                    entries.Add(new AchievementHistoryEntry
                    {
                        AppId = appId,
                        GameName = gameName,
                        AchievementName = TrackingConfirmation.Title,
                        Description = TrackingConfirmation.Description(gameName),
                        IconPath = EmbeddedAssets.GetTrackingConfiguredIconPath(),
                        EarnedTime = configuredTime
                    });
                }

                if (states == null)
                    continue;

                // Load the schema once per game rather than once per earned achievement.
                var definitions = gameInfo != null ? GameCache.LoadDefinitions(gameInfo) : null;
                var metadataDir = gameInfo != null ? Path.GetDirectoryName(gameInfo.MetadataPath)! : "";

                foreach (var (achName, state) in states)
                {
                    if (!state.Earned)
                        continue;

                    var resolved = AchievementMetadata.ResolvePreferringSchema(
                        state, definitions, metadataDir, achName, _config.Language);

                    if (gameInfo == null && resolved == null)
                        continue;

                    entries.Add(new AchievementHistoryEntry
                    {
                        AppId = appId,
                        GameName = gameName,
                        AchievementName = resolved?.DisplayName ?? achName,
                        Description = resolved?.Description ?? "",
                        IconPath = resolved?.IconPath,
                        EarnedTime = state.EarnedTime
                    });
                }
            }
        }

        // Synthetic entry with exe modified date
        var exePath = Environment.ProcessPath;
        var installedTime = exePath != null && File.Exists(exePath) ? new DateTimeOffset(File.GetLastWriteTimeUtc(exePath), TimeSpan.Zero).ToUnixTimeSeconds() : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        entries.Add(new AchievementHistoryEntry
        {
            AppId = "",
            GameName = "Achievement Overlay",
            AchievementName = "Achievement Connoisseur",
            Description = "Install and configure Achievement Overlay",
            IconPath = EmbeddedAssets.GetConnoisseurIconPath(),
            EarnedTime = installedTime
        });

        return entries.OrderByDescending(e => e.EarnedTime).Take(count).ToList();
    }
}
