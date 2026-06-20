using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace AchievementOverlay.GbeConfig;

/// <summary>
/// Resolves a Steam AppID for a game directory: explicit value, then any local
/// <c>steam_appid.txt</c>, then a key in local <c>*.ini</c>/<c>*.cfg</c> files, then
/// a Steam store search by name as a last resort.
/// </summary>
public static partial class AppIdResolver
{
    [GeneratedRegex(@"(?:steam[_]?)?app[_]?id\s*[=:]\s*(\d{2,})", RegexOptions.IgnoreCase)]
    private static partial Regex IniAppIdRegex();

    [GeneratedRegex(@"data-ds-appid\s*=\s*""(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex StoreAppIdRegex();

    /// <summary>
    /// Reads an AppID from the first <c>steam_appid.txt</c> found under the game directory.
    /// </summary>
    public static string? FromAppIdTxt(string gameDir)
    {
        if (!Directory.Exists(gameDir))
            return null;

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var file in Directory.EnumerateFiles(gameDir, "steam_appid.txt", options))
        {
            try
            {
                var id = File.ReadAllText(file)
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(id) && id.All(char.IsDigit))
                    return id;
            }
            catch (IOException)
            {
                // Unreadable — try the next match.
            }
        }
        return null;
    }

    /// <summary>
    /// Scans local <c>*.ini</c>/<c>*.cfg</c> files for a <c>steam_appid</c>-style key.
    /// </summary>
    public static string? FromIniFiles(string gameDir)
    {
        if (!Directory.Exists(gameDir))
            return null;

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var pattern in new[] { "*.ini", "*.cfg" })
        {
            foreach (var file in Directory.EnumerateFiles(gameDir, pattern, options))
            {
                try
                {
                    var id = ParseAppIdFromText(File.ReadAllText(file));
                    if (id != null)
                        return id;
                }
                catch (IOException)
                {
                    // Unreadable — try the next file.
                }
            }
        }
        return null;
    }

    /// <summary>Extracts the first <c>steam_appid</c>-style assignment from arbitrary text.</summary>
    public static string? ParseAppIdFromText(string text)
    {
        var match = IniAppIdRegex().Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Extracts the first <c>data-ds-appid</c> value from Steam store search HTML.</summary>
    public static string? ParseAppIdFromStoreHtml(string html)
    {
        var match = StoreAppIdRegex().Match(html);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Searches the Steam store by game name and returns the first matching AppID.
    /// </summary>
    public static async Task<string?> FromStoreSearchAsync(string gameName, HttpClient http, CancellationToken ct = default)
    {
        var url = $"https://store.steampowered.com/search/?term={Uri.EscapeDataString(gameName)}";
        try
        {
            var html = await http.GetStringAsync(url, ct);
            return ParseAppIdFromStoreHtml(html);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}
