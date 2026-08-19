using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace AchievementOverlay;

public sealed class AppConfig
{
    private static readonly string ExeDir = AppContext.BaseDirectory;
    private static readonly string SettingsPath = Path.Combine(ExeDir, "config.json");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private DateTime _lastWriteTimeUtc;
    private SettingsData _settings = null!;
    private readonly object _lock = new();
    private readonly string _settingsFilePath;

    public AppConfig()
    {
        _settingsFilePath = SettingsPath;
        _settings = Load();
    }

    /// <summary>
    /// Internal constructor for testing — accepts a custom settings path.
    /// </summary>
    internal AppConfig(string settingsPath)
    {
        _settingsFilePath = settingsPath;
        _settings = Load(settingsPath);
    }

    public string[] GamesPaths { get { Reload(); return _gamesPaths ??= ParseGamesPaths(_settings.GamesPaths); } }
    public string[] GseSavesPaths { get { Reload(); return _gseSavesPaths ??= ParseGamesPaths(_settings.GseSavesPaths); } }
    public string Language { get { Reload(); return _settings.Language; } }
    public string Font { get { Reload(); return _settings.Font; } }
    public NotificationScale Scale { get { Reload(); return _settings.Scale; } }
    public bool SoundEnabled { get { Reload(); return _settings.SoundEnabled; } }
    public string SoundPath { get { Reload(); return _settings.SoundPath; } }
    public int DisplayDuration { get { Reload(); return _settings.DisplayDuration; } }
    public bool UseGameOverlaySettings { get { Reload(); return _settings.UseGameOverlaySettings; } }
    public string RecentAchievementsShortcut { get { Reload(); return _settings.RecentAchievementsShortcut; } }
    public int RecentAchievementsCount { get { Reload(); return _settings.RecentAchievementsCount; } }
    public string? SteamWebApiKey { get { Reload(); return _settings.SteamWebApiKey; } }
    public string? FirecrawlApiKey { get { Reload(); return _settings.FirecrawlApiKey; } }

    private string[]? _gseSavesPaths;
    private string[]? _gamesPaths;

    public SettingsData GetCurrent()
    {
        Reload();
        return _settings;
    }

    public void UpdateConfigValue(string propertyName, object value)
    {
        UpdateConfigValue(propertyName, value, _settingsFilePath);
    }

    internal void UpdateConfigValue(string propertyName, object value, string settingsPath)
    {
        UpdateConfigValues(new Dictionary<string, object?> { [propertyName] = value }, settingsPath);
    }

    /// <summary>
    /// Writes several settings in one read-modify-write pass, keyed by <see cref="SettingsData"/>
    /// property name. The settings dialog saves through here so a save is one file write rather than
    /// one per field — every write bumps the file's timestamp and triggers a reload.
    /// </summary>
    public void UpdateConfigValues(IReadOnlyDictionary<string, object?> values)
    {
        UpdateConfigValues(values, _settingsFilePath);
    }

    internal void UpdateConfigValues(IReadOnlyDictionary<string, object?> values, string settingsPath)
    {
        lock (_lock)
        {
            var names = string.Join(", ", values.Keys);
            string json;
            try
            {
                if (!File.Exists(settingsPath))
                {
                    Logger.Warn($"Config file not found, cannot update '{names}'");
                    return;
                }
                json = File.ReadAllText(settingsPath);
            }
            catch (IOException ex)
            {
                Logger.Warn($"Could not read config to update '{names}': {ex.Message}");
                return;
            }

            Dictionary<string, JsonElement> dict;
            try
            {
                dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions)
                       ?? new Dictionary<string, JsonElement>();
            }
            catch (JsonException ex)
            {
                Logger.Warn($"Config file is malformed, could not update '{names}': {ex.Message}");
                return;
            }

            foreach (var (propertyName, value) in values)
            {
                var camelKey = JsonNamingPolicy.CamelCase.ConvertName(propertyName);
                dict[camelKey] = JsonSerializer.SerializeToElement(value, JsonOptions);
            }

            var updated = JsonSerializer.Serialize(dict, JsonOptions);
            try
            {
                File.WriteAllText(settingsPath, updated);
                _lastWriteTimeUtc = File.GetLastWriteTimeUtc(settingsPath);
            }
            catch (IOException ex)
            {
                Logger.Warn($"Could not write config for '{names}': {ex.Message}");
            }
            _settings = Deserialize(updated);
            InvalidateCaches();
        }
    }

    private SettingsData Load(string? path = null)
    {
        var filePath = path ?? SettingsPath;
        if (File.Exists(filePath))
        {
            var writeTime = File.GetLastWriteTimeUtc(filePath);
            var json = File.ReadAllText(filePath);
            var result = Deserialize(json);
            _lastWriteTimeUtc = writeTime;
            return result;
        }

        throw new FileNotFoundException($"Config file not found: '{filePath}'. The file should be in the same directory as the executable.");
    }

    private void Reload(string? path = null)
    {
        var filePath = path ?? _settingsFilePath;
        if (!File.Exists(filePath))
            return;

        var currentWriteTime = File.GetLastWriteTimeUtc(filePath);
        if (currentWriteTime <= _lastWriteTimeUtc)
            return;

        lock (_lock)
        {
            // Double-check after acquiring lock
            currentWriteTime = File.GetLastWriteTimeUtc(filePath);
            if (currentWriteTime <= _lastWriteTimeUtc)
                return;

            try
            {
                var json = File.ReadAllText(filePath);
                _settings = Deserialize(json);
                _lastWriteTimeUtc = currentWriteTime;
                InvalidateCaches();
            }
            catch (JsonException)
            {
                // Malformed JSON (e.g. partially-written file) — keep last good config
                // Don't advance _lastWriteTimeUtc so the file will be re-read on next access
            }
            catch (IOException)
            {
                // File locked or inaccessible — keep last good config
                // Don't advance _lastWriteTimeUtc so the file will be re-read on next access
            }
            catch (InvalidOperationException)
            {
                // Failed validation (e.g. a required setting momentarily deleted while the user
                // edits the file) — keep last good config. Without this the exception escapes
                // every config property getter and takes the app down mid-session.
                // Don't advance _lastWriteTimeUtc so the file will be re-read on next access
            }
        }
    }

    private void InvalidateCaches()
    {
        _gseSavesPaths = null;
        _gamesPaths = null;
    }

    private static SettingsData Deserialize(string json)
    {
        var result = JsonSerializer.Deserialize<SettingsData>(json, JsonOptions) ?? new SettingsData();
        Validate(result);
        return result;
    }

    private static void Validate(SettingsData settings)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(settings.GseSavesPaths))
            errors.Add("'gseSavesPaths' is missing or empty");
        // Absent, not empty: a user with only self-describing games has no Steam game roots and
        // must not be forced to invent one. A missing key still errors, so a typo is still loud.
        if (settings.GamesPaths == null) errors.Add("'gamesPaths' is missing");
        if (settings.DisplayDuration <= 0) errors.Add("'displayDuration' is missing or invalid");
        if (settings.RecentAchievementsCount <= 0) errors.Add("'recentAchievementsCount' is missing or invalid");
        if (errors.Count > 0)
            throw new InvalidOperationException("Invalid config: " + string.Join("\n", errors));
    }

    public static string ExpandEnvironmentVariables(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        return Environment.ExpandEnvironmentVariables(path);
    }

    /// <summary>
    /// Folder variables an absolute path is packed back into, tried by expansion length so a nested
    /// one (%localappdata%) wins over the parent it sits under (%userprofile%).
    /// </summary>
    private static readonly string[] CollapsibleVariables =
    {
        "%appdata%", "%localappdata%", "%programdata%", "%programfiles(x86)%", "%programfiles%", "%public%", "%userprofile%"
    };

    /// <summary>
    /// The inverse of <see cref="ExpandEnvironmentVariables"/>: rewrites an absolute path back into
    /// variable form when it sits under a known folder, leaving anything else untouched. The folder
    /// picker only ever hands back absolute paths, so without this, picking the GSE Saves folder
    /// would replace the portable default '%appdata%\GSE Saves' with one machine's user profile —
    /// and a config that travels between machines would stop resolving on the other one.
    /// </summary>
    public static string CollapseEnvironmentVariables(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        string? bestVariable = null;
        var bestLength = 0;

        foreach (var variable in CollapsibleVariables)
        {
            // An undefined variable expands to itself, which is never a path prefix worth using.
            var expanded = ExpandEnvironmentVariables(variable);
            if (expanded == variable || string.IsNullOrEmpty(expanded))
                continue;

            expanded = Path.TrimEndingDirectorySeparator(expanded);
            if (expanded.Length > bestLength && StartsWithFolder(path, expanded))
            {
                bestVariable = variable;
                bestLength = expanded.Length;
            }
        }

        return bestVariable == null ? path : bestVariable + path[bestLength..];
    }

    /// <summary>
    /// True when <paramref name="path"/> is <paramref name="folder"/> or sits inside it. The
    /// separator check is what stops 'C:\Users\Bobby' from matching the folder 'C:\Users\Bob'.
    /// </summary>
    private static bool StartsWithFolder(string path, string folder)
    {
        if (!path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            return false;

        return path.Length == folder.Length || path[folder.Length] is '\\' or '/';
    }

    /// <summary>
    /// Splits a semicolon-separated setting <em>without</em> expanding environment variables. The
    /// settings dialog round-trips entries straight back into config, so '%appdata%\GSE Saves' has
    /// to stay written that way rather than being frozen to one machine's absolute path.
    /// </summary>
    public static string[] SplitRawPaths(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// The same list, expanded and ready to use. Splits through <see cref="SplitRawPaths"/> so the
    /// ';' convention is stated once — the raw and expanded readings can't disagree about it.
    /// </summary>
    public static string[] ParseGamesPaths(string? gamesPaths) =>
        SplitRawPaths(gamesPaths)
            .Select(ExpandEnvironmentVariables)
            .Where(p => !string.IsNullOrEmpty(p))
            .ToArray();

    private static string ExpandAndCache(ref string? cached, string raw)
    {
        return cached ??= ExpandEnvironmentVariables(raw);
    }

    // --- Registry auto-start ---

    private const string RegistryRunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "AchievementOverlay";

    public static bool IsStartWithWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, false);
        return key?.GetValue(AppName) != null;
    }

    public static void SetStartWithWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, true);
        if (key == null) return;

        if (enabled)
        {
            var exePath = Environment.ProcessPath;
            if (exePath != null)
                key.SetValue(AppName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(AppName, false);
        }
    }
}

/// <summary>
/// Settings model. Defaults come from the embedded config/default.json resource —
/// do not add default values to properties here.
/// </summary>
public sealed class SettingsData
{
    /// <summary>
    /// Null means the key is absent (a config error); an empty string is a valid choice for a user
    /// whose games all describe their own achievements in the unlock file.
    /// </summary>
    [JsonPropertyName("gamesPaths")]
    public string? GamesPaths { get; set; }

    [JsonPropertyName("gseSavesPaths")]
    public string GseSavesPaths { get; set; } = "";

    [JsonPropertyName("language")]
    public string Language { get; set; } = "";

    /// <summary>
    /// Font family for the popup's text. Empty means the built-in default — resolved at render
    /// time rather than here, so a family that is later uninstalled degrades to the default
    /// instead of blanking the notification.
    /// </summary>
    [JsonPropertyName("font")]
    public string Font { get; set; } = "";

    /// <summary>Absent reads as the default share of the screen. The converter is on the type.</summary>
    [JsonPropertyName("scale")]
    public NotificationScale Scale { get; set; }

    [JsonPropertyName("soundEnabled")]
    public bool SoundEnabled { get; set; }

    [JsonPropertyName("soundPath")]
    public string SoundPath { get; set; } = "";

    [JsonPropertyName("displayDuration")]
    public int DisplayDuration { get; set; }

    /// <summary>
    /// Whether a game's own <c>steam_settings/</c> may override the unlock sound, the display
    /// duration and the font for that game's popups. Absent reads as off, so an existing install
    /// never changes behaviour because of an ini someone wrote years ago and forgot; a fresh config
    /// ships with it on.
    /// </summary>
    [JsonPropertyName("useGameOverlaySettings")]
    public bool UseGameOverlaySettings { get; set; }

    [JsonPropertyName("recentAchievementsShortcut")]
    public string RecentAchievementsShortcut { get; set; } = "";

    [JsonPropertyName("recentAchievementsCount")]
    public int RecentAchievementsCount { get; set; }

    // --- Config generator settings (optional; used by the Add-game dialog) ---

    [JsonPropertyName("steamWebApiKey")]
    public string? SteamWebApiKey { get; set; }

    [JsonPropertyName("firecrawlApiKey")]
    public string? FirecrawlApiKey { get; set; }

    // --- App-managed state (not user-facing) ---

    /// <summary>
    /// Maps appid → unix time (seconds) when the synthetic "Achievement tracking configured"
    /// notification first fired for that game. Presence means it has been shown (so it never
    /// fires again); the value is used to timestamp the Recent-achievements entry. Updated at runtime.
    /// </summary>
    [JsonPropertyName("trackingConfigured")]
    public Dictionary<string, long>? TrackingConfigured { get; set; }
}
