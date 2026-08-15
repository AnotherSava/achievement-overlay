using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AchievementOverlay.GbeConfig;

/// <summary>
/// A single entry of the GBE <c>achievements.json</c> array. Note <c>hidden</c> is a
/// string <c>"0"</c>/<c>"1"</c>, not a bool — a GBE quirk. Icon paths are relative to
/// the <c>steam_settings/</c> folder.
/// </summary>
public sealed class GbeAchievement
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("hidden")]
    public string Hidden { get; init; } = "0";

    [JsonPropertyName("icon")]
    public string Icon { get; init; } = "";

    [JsonPropertyName("icongray")]
    public string IconGray { get; init; } = "";
}

/// <summary>
/// Converts the Steam Web API achievement schema into the GBE <c>achievements.json</c>
/// format and serializes it.
/// </summary>
public static class AchievementSchemaWriter
{
    /// <summary>Relative directory (under steam_settings/) where icons are written.</summary>
    public const string ImageDir = "achievement_images";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Don't escape non-ASCII — achievement text is full of accented characters and emoji.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static List<GbeAchievement> Build(IEnumerable<SteamSchemaAchievement> schema)
    {
        return schema.Select(a => new GbeAchievement
        {
            Name = a.Name,
            DisplayName = a.DisplayName,
            Description = a.Description,
            Hidden = a.Hidden != 0 ? "1" : "0",
            Icon = $"{ImageDir}/{a.Name}.jpg",
            IconGray = $"{ImageDir}/{a.Name}_gray.jpg"
        }).ToList();
    }

    /// <summary>
    /// Patches descriptions for achievements whose API name appears in
    /// <paramref name="descriptions"/>. Used to fill in hidden-achievement text scraped
    /// from SteamDB.
    /// </summary>
    public static void ApplyDescriptions(List<GbeAchievement> achievements, IReadOnlyDictionary<string, string> descriptions)
    {
        for (var i = 0; i < achievements.Count; i++)
        {
            var a = achievements[i];
            if (descriptions.TryGetValue(a.Name, out var desc) && !string.IsNullOrWhiteSpace(desc) && desc != a.Description)
            {
                achievements[i] = new GbeAchievement
                {
                    Name = a.Name,
                    DisplayName = a.DisplayName,
                    Description = desc,
                    Hidden = a.Hidden,
                    Icon = a.Icon,
                    IconGray = a.IconGray
                };
            }
        }
    }

    public static string Serialize(List<GbeAchievement> achievements) =>
        JsonSerializer.Serialize(achievements, JsonOptions);
}
