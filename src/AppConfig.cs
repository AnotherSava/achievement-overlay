using System.IO;
using System.Reflection;
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
    public string GseSavesPath { get { Reload(); return ExpandAndCache(ref _gseSavesPathExpanded, _settings.GseSavesPath); } }
    public string Language { get { Reload(); return _settings.Language; } }
    public bool SoundEnabled { get { Reload(); return _settings.SoundEnabled; } }
    public string SoundPath { get { Reload(); return _settings.SoundPath; } }
    public int DisplayDuration { get { Reload(); return _settings.DisplayDuration; } }
    public string RecentAchievementsShortcut { get { Reload(); return _settings.RecentAchievementsShortcut; } }
    public int RecentAchievementsCount { get { Reload(); return _settings.RecentAchievementsCount; } }

    private string? _gseSavesPathExpanded;
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
        lock (_lock)
        {
            string json;
            try
            {
                if (!File.Exists(settingsPath))
                {
                    Logger.Warn($"Config file not found, cannot update '{propertyName}'");
                    return;
                }
                json = File.ReadAllText(settingsPath);
            }
            catch (IOException ex)
            {
                Logger.Warn($"Could not read config to update '{propertyName}': {ex.Message}");
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
                Logger.Warn($"Config file is malformed, could not update '{propertyName}': {ex.Message}");
                return;
            }

            var camelKey = JsonNamingPolicy.CamelCase.ConvertName(propertyName);
            dict[camelKey] = JsonSerializer.SerializeToElement(value, JsonOptions);

            var updated = JsonSerializer.Serialize(dict, JsonOptions);
            try
            {
                File.WriteAllText(settingsPath, updated);
                _lastWriteTimeUtc = File.GetLastWriteTimeUtc(settingsPath);
            }
            catch (IOException ex)
            {
                Logger.Warn($"Could not write config for '{propertyName}': {ex.Message}");
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
        }
    }

    private void InvalidateCaches()
    {
        _gseSavesPathExpanded = null;
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
        if (string.IsNullOrWhiteSpace(settings.GseSavesPath))
            errors.Add("'gseSavesPath' is missing or empty");
        else if (!Directory.Exists(ExpandEnvironmentVariables(settings.GseSavesPath)))
            errors.Add("'gseSavesPath' directory does not exist");
        if (string.IsNullOrWhiteSpace(settings.GamesPaths)) errors.Add("'gamesPaths' is missing or empty");
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

    public static string[] ParseGamesPaths(string? gamesPaths)
    {
        if (string.IsNullOrWhiteSpace(gamesPaths))
            return Array.Empty<string>();

        return gamesPaths
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ExpandEnvironmentVariables)
            .Where(p => !string.IsNullOrEmpty(p))
            .ToArray();
    }

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
            var exePath = Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location;
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
    [JsonPropertyName("gamesPaths")]
    public string GamesPaths { get; set; } = "";

    [JsonPropertyName("gseSavesPath")]
    public string GseSavesPath { get; set; } = "";

    [JsonPropertyName("language")]
    public string Language { get; set; } = "";

    [JsonPropertyName("soundEnabled")]
    public bool SoundEnabled { get; set; }

    [JsonPropertyName("soundPath")]
    public string SoundPath { get; set; } = "";

    [JsonPropertyName("displayDuration")]
    public int DisplayDuration { get; set; }

    [JsonPropertyName("recentAchievementsShortcut")]
    public string RecentAchievementsShortcut { get; set; } = "";

    [JsonPropertyName("recentAchievementsCount")]
    public int RecentAchievementsCount { get; set; }
}
