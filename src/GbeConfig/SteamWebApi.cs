using System.Net.Http;
using System.Text.Json;

namespace AchievementOverlay.GbeConfig;

/// <summary>
/// A single achievement from <c>ISteamUserStats/GetSchemaForGame</c>.
/// Hidden achievements have their <see cref="Description"/> redacted by Steam.
/// </summary>
public sealed record SteamSchemaAchievement(
    string Name,
    string DisplayName,
    int Hidden,
    string Description,
    string Icon,
    string IconGray);

/// <summary>
/// Thin <see cref="HttpClient"/> wrapper over the public Steam Web API endpoints the
/// achievement use-case needs. Parsing is split into pure static methods for testing.
/// </summary>
public static class SteamWebApi
{
    /// <summary>
    /// Fetches the achievement schema for an app. Throws <see cref="HttpRequestException"/>
    /// on transport failure and <see cref="InvalidOperationException"/> on an unexpected
    /// (e.g. unauthorized / empty) response.
    /// </summary>
    public static async Task<List<SteamSchemaAchievement>> GetAchievementSchemaAsync(
        string apiKey, string appId, HttpClient http, CancellationToken ct = default)
    {
        var url = $"https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?key={apiKey}&appid={appId}";
        var json = await http.GetStringAsync(url, ct);
        return ParseSchema(json);
    }

    public static List<SteamSchemaAchievement> ParseSchema(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<SteamSchemaAchievement>();

        if (!doc.RootElement.TryGetProperty("game", out var game)
            || !game.TryGetProperty("availableGameStats", out var stats)
            || !stats.TryGetProperty("achievements", out var achievements)
            || achievements.ValueKind != JsonValueKind.Array)
        {
            // No achievements block — either the app has none or the key is unauthorized.
            return result;
        }

        foreach (var a in achievements.EnumerateArray())
        {
            result.Add(new SteamSchemaAchievement(
                Name: GetString(a, "name"),
                DisplayName: GetString(a, "displayName"),
                Hidden: GetInt(a, "hidden"),
                Description: GetString(a, "description"),
                Icon: GetString(a, "icon"),
                IconGray: GetString(a, "icongray")));
        }
        return result;
    }

    private static string GetString(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int GetInt(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;
}
