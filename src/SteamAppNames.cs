using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace AchievementOverlay;

/// <summary>
/// Steam store names for the games the app shows, so a game is labelled by its real name rather than by
/// the folder it was found in. Callers decide when to look names up — <c>TrayApplicationContext</c>, through
/// its <c>HasSteamAppId</c> rule, and the Add game wizard — and only names missing from the cache are
/// fetched. The cache is a file beside <c>config.json</c>, so a name is fetched once. The file is derived data and deliberately not part of the config: nothing edits
/// it, and losing it costs only a re-fetch.
/// </summary>
public sealed class SteamAppNames : IDisposable
{
    private readonly string _cachePath;
    private readonly ConcurrentDictionary<string, string> _names;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly Lock _saveLock = new();
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public SteamAppNames(string cachePath)
    {
        _cachePath = cachePath;
        _names = new ConcurrentDictionary<string, string>(Load(cachePath));
    }

    /// <summary>The cache file the app uses, beside <c>config.json</c>.</summary>
    public static string DefaultCachePath => Path.Combine(Path.GetDirectoryName(AppConfig.ConfigFilePath)!, "names.json");

    /// <summary>The store name of <paramref name="appId"/>, or null when none has been looked up.</summary>
    public string? Get(string appId) => _names.TryGetValue(appId, out var name) ? name : null;

    /// <summary>
    /// The name of a game wherever the app shows one: its store name, else the folder name the scan gave it,
    /// else null. For a place that formats the appid beside the name and so needs to know there is none.
    /// </summary>
    public string? Name(string appId, GameInfo? game) => Get(appId) ?? game?.GameName;

    /// <summary>What to call a game wherever the app shows one: its <see cref="Name"/>, else the appid.</summary>
    public string Label(string appId, GameInfo? game) => Name(appId, game) ?? appId;

    /// <summary>
    /// Looks up each appid not already known, one at a time, and saves the cache when anything was added.
    /// Never throws; a failed lookup is logged and leaves that game labelled as before.
    /// </summary>
    public async Task FetchAsync(IEnumerable<string> appIds)
    {
        var added = false;
        foreach (var appId in appIds.Where(id => !_names.ContainsKey(id)).Distinct().ToList())
        {
            try
            {
                var json = await _http.GetStringAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic");
                if (ParseName(json, appId) is { } name)
                {
                    _names[appId] = name;
                    added = true;
                }
                else
                {
                    Logger.Info($"Steam store has no name for appid {appId}.");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                Logger.Info($"Could not look up the store name of appid {appId}: {ex.Message}");
            }
            catch (ObjectDisposedException)
            {
                break; // the app is exiting; what arrived so far is still saved below
            }
        }

        if (added)
            Save();
    }

    /// <summary>
    /// The <c>data.name</c> of <paramref name="appId"/> in a Steam store <c>appdetails</c> response, or null
    /// when the store reports no such app.
    /// </summary>
    public static string? ParseName(string json, string appId)
    {
        // TryGetProperty throws on anything but an object, and the store has been known to answer null.
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty(appId, out var entry) || entry.ValueKind != JsonValueKind.Object
            || !entry.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True
            || !entry.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = Clean(name.GetString());
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// A store name without the trademark signs publishers append ("Starfall™ Remastered"). Casing is left as
    /// the publisher writes it: a title in all capitals is usually the publisher's choice, and recasing would
    /// also break names that are mixed on purpose, such as "MixedCase:Title".
    /// </summary>
    public static string Clean(string? name) =>
        string.Join(' ', (name ?? "").Replace("™", "").Replace("®", "").Replace("©", "").Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static Dictionary<string, string> Load(string path)
    {
        if (!File.Exists(path))
            return new Dictionary<string, string>();

        try
        {
            // Cleaned here too, so a cache written before a cleaning rule existed reads the same as a fresh lookup.
            var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new Dictionary<string, string>();
            return stored.Select(entry => (entry.Key, Name: Clean(entry.Value))).Where(entry => entry.Name.Length > 0).ToDictionary(entry => entry.Key, entry => entry.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Logger.Warn($"Ignoring the game name cache '{path}', which could not be read, so names are looked up again: {ex.Message}");
            return new Dictionary<string, string>();
        }
    }

    private void Save()
    {
        lock (_saveLock)
        {
            try
            {
                var temp = _cachePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(new SortedDictionary<string, string>(_names), Indented));
                File.Move(temp, _cachePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.Warn($"Could not save the game name cache '{_cachePath}', so names will be looked up again next start: {ex.Message}");
            }
        }
    }

    public void Dispose() => _http.Dispose();
}
