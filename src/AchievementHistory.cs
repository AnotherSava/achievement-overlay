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
    private readonly Action<string>? _log;

    public AchievementHistory(AppConfig config, GameCache gameCache, Action<string>? log = null)
    {
        _config = config;
        _gameCache = gameCache;
        _log = log;
    }

    /// <summary>
    /// Returns the most recent earned achievements, sorted by earned_time descending.
    /// Only includes achievements for tracked games (present in GameCache).
    /// Always includes a synthetic "Achievement Overlay installed" entry.
    /// </summary>
    public List<AchievementHistoryEntry> GetRecent(int count)
    {
        var entries = new List<AchievementHistoryEntry>();

        var gseSavesPath = _config.GseSavesPath;
        if (Directory.Exists(gseSavesPath))
        {
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
                    var definitions = GameCache.LoadDefinitions(gameInfo);

                    foreach (var (achName, state) in states)
                    {
                        if (!state.Earned)
                            continue;

                        var displayName = achName;
                        var description = "";
                        string? iconPath = null;

                        if (definitions != null)
                        {
                            var def = AchievementMetadata.FindDefinition(definitions, achName);
                            if (def != null)
                            {
                                var language = _config.Language;
                                var resolved = AchievementMetadata.GetDisplayText(def.DisplayName, language);
                                if (!string.IsNullOrEmpty(resolved)) displayName = resolved;
                                description = AchievementMetadata.GetDisplayText(def.Description, language);
                                var metadataDir = Path.GetDirectoryName(gameInfo.MetadataPath)!;
                                iconPath = AchievementMetadata.ResolveIconPath(def, metadataDir);
                            }
                        }

                        entries.Add(new AchievementHistoryEntry
                        {
                            AppId = appId,
                            GameName = gameInfo.GameName,
                            AchievementName = displayName,
                            Description = description,
                            IconPath = iconPath,
                            EarnedTime = state.EarnedTime
                        });
                    }
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"Error reading achievements for appid {appId}: {ex.Message}");
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
