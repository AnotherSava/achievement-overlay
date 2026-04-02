using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AchievementOverlay;

/// <summary>
/// Represents the unlock state of a single achievement from GSE Saves achievements.json.
/// Format: {"ACH01": {"earned": true, "earned_time": 1774855788}, ...}
/// </summary>
public sealed class AchievementUnlockState
{
    [JsonPropertyName("earned")]
    public bool Earned { get; set; }

    [JsonPropertyName("earned_time")]
    public long EarnedTime { get; set; }
}

/// <summary>
/// Represents a single achievement entry from the game's steam_settings/achievements.json.
/// Display text fields (displayName, description) can be either a plain string or a
/// multi-language object like {"english": "...", "german": "..."}.
/// </summary>
public sealed class AchievementDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("displayName")]
    public JsonElement? DisplayName { get; set; }

    [JsonPropertyName("description")]
    public JsonElement? Description { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("icon_gray")]
    public string? IconGray { get; set; }

    [JsonPropertyName("hidden")]
    public int Hidden { get; set; }
}

public static class AchievementMetadata
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Parses the GSE Saves achievements.json (dict of name -> unlock state).
    /// </summary>
    public static Dictionary<string, AchievementUnlockState> ParseUnlockStates(string json)
    {
        return JsonSerializer.Deserialize<Dictionary<string, AchievementUnlockState>>(json, JsonOptions)
               ?? new Dictionary<string, AchievementUnlockState>();
    }

    /// <summary>
    /// Parses the game's steam_settings/achievements.json (array of definitions).
    /// </summary>
    public static List<AchievementDefinition> ParseDefinitions(string json)
    {
        return JsonSerializer.Deserialize<List<AchievementDefinition>>(json, JsonOptions)
               ?? new List<AchievementDefinition>();
    }

    /// <summary>
    /// Resolves display text from a JsonElement that may be a plain string or a
    /// multi-language object. Falls back to english, then first available value.
    /// </summary>
    public static string GetDisplayText(JsonElement? element, string language)
    {
        if (element == null || element.Value.ValueKind == JsonValueKind.Undefined
                           || element.Value.ValueKind == JsonValueKind.Null)
            return "";

        if (element.Value.ValueKind == JsonValueKind.String)
            return element.Value.GetString() ?? "";

        if (element.Value.ValueKind == JsonValueKind.Object)
        {
            // Try requested language first
            if (element.Value.TryGetProperty(language, out var langValue)
                && langValue.ValueKind == JsonValueKind.String)
                return langValue.GetString() ?? "";

            // Fallback to english
            Logger.Warn($"Language '{language}' not available, falling back to english");
            if (language != "english"
                && element.Value.TryGetProperty("english", out var engValue)
                && engValue.ValueKind == JsonValueKind.String)
                return engValue.GetString() ?? "";

            // Fallback to first available value
            foreach (var prop in element.Value.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                    return prop.Value.GetString() ?? "";
            }
        }

        return "";
    }

    /// <summary>
    /// Finds a definition by achievement internal name (case-insensitive).
    /// </summary>
    public static AchievementDefinition? FindDefinition(
        IEnumerable<AchievementDefinition> definitions, string achievementName)
    {
        return definitions.FirstOrDefault(
            d => string.Equals(d.Name, achievementName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves the icon file path for an achievement. Looks for the icon filename
    /// in the game's steam_settings/images/ directory relative to the metadata path.
    /// </summary>
    public static string? ResolveIconPath(AchievementDefinition definition, string metadataDir)
    {
        var iconName = definition.Icon;
        if (string.IsNullOrEmpty(iconName))
            return null;

        // Resolve and validate path stays within metadata directory (prevent path traversal)
        var metaDirFull = Path.GetFullPath(metadataDir) + Path.DirectorySeparatorChar;

        // Icon paths in the schema are relative to steam_settings/ (e.g. "img/abc123.jpg")
        var exactPath = Path.GetFullPath(Path.Combine(metadataDir, iconName));
        if (exactPath.StartsWith(metaDirFull, StringComparison.OrdinalIgnoreCase) && File.Exists(exactPath))
            return exactPath;

        // Try with common extensions
        foreach (var ext in new[] { ".jpg", ".png", ".bmp", ".ico" })
        {
            var withExt = exactPath + ext;
            if (withExt.StartsWith(metaDirFull, StringComparison.OrdinalIgnoreCase) && File.Exists(withExt))
                return withExt;
        }

        return null;
    }

    /// <summary>
    /// Resolves display name, description, and icon path for an achievement.
    /// Returns null if game or definition not found.
    /// </summary>
    public static ResolvedAchievement? Resolve(GameCache gameCache, string appId, string achievementName, string language)
    {
        var gameInfo = gameCache.Lookup(appId);
        if (gameInfo == null)
            return null;

        var definitions = GameCache.LoadDefinitions(gameInfo);
        if (definitions == null)
            return null;

        var definition = FindDefinition(definitions, achievementName);
        if (definition == null)
            return null;

        var displayName = GetDisplayText(definition.DisplayName, language);
        var description = GetDisplayText(definition.Description, language);

        if (string.IsNullOrEmpty(displayName))
            displayName = achievementName;

        var metadataDir = Path.GetDirectoryName(gameInfo.MetadataPath)!;
        var iconPath = ResolveIconPath(definition, metadataDir);

        return new ResolvedAchievement
        {
            GameName = gameInfo.GameName,
            DisplayName = displayName,
            Description = description,
            IconPath = iconPath
        };
    }
}

public sealed class ResolvedAchievement
{
    public required string GameName { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public string? IconPath { get; init; }
}
