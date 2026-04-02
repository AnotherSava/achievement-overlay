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

    public string GseSavesPath { get { Reload(); return ExpandAndCache(ref _gseSavesPathExpanded, _settings.GseSavesPath); } }
    public string[] GamesPaths { get { Reload(); return _gamesPaths ??= ParseGamesPaths(_settings.GamesPaths); } }
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
                json = File.Exists(settingsPath)
                    ? File.ReadAllText(settingsPath)
                    : GetEmbeddedDefault();
            }
            catch (IOException)
            {
                // File is transiently locked — bail out without overwriting to avoid
                // destroying on-disk data with potentially stale in-memory defaults
                // (e.g. if _settings was populated from embedded defaults after a startup read failure).
                // The setting won't persist until the file is accessible again.
                return;
            }

            Dictionary<string, JsonElement> dict;
            try
            {
                dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions)
                       ?? new Dictionary<string, JsonElement>();
            }
            catch (JsonException)
            {
                // Existing file is malformed (e.g. user is mid-edit) — bail out without
                // overwriting to avoid destroying on-disk edits with stale in-memory data.
                // The setting won't persist until the file is valid again.
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
            catch (IOException)
            {
                // File is transiently locked for writing — the setting won't persist
                // until the file is accessible again, but update in-memory state so the
                // current session reflects the change.
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

        // Fall back to embedded default and try to write it out
        var defaultJson2 = GetEmbeddedDefault();
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(filePath, defaultJson2);
            _lastWriteTimeUtc = File.GetLastWriteTimeUtc(filePath);
        }
        catch (Exception)
        {
            // Directory may be read-only — use defaults in memory
        }
        return Deserialize(defaultJson2);
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
        // Merge with embedded defaults so missing properties get filled in
        var defaults = JsonSerializer.Deserialize<SettingsData>(GetEmbeddedDefault(), JsonOptions) ?? new SettingsData();
        var result = JsonSerializer.Deserialize<SettingsData>(json, JsonOptions) ?? defaults;
        if (string.IsNullOrEmpty(result.GseSavesPath)) result.GseSavesPath = defaults.GseSavesPath;
        if (string.IsNullOrEmpty(result.Language)) result.Language = defaults.Language;
        if (result.DisplayDuration <= 0) result.DisplayDuration = defaults.DisplayDuration;
        if (string.IsNullOrEmpty(result.RecentAchievementsShortcut)) result.RecentAchievementsShortcut = defaults.RecentAchievementsShortcut;
        if (result.RecentAchievementsCount <= 0) result.RecentAchievementsCount = defaults.RecentAchievementsCount;
        return result;
    }

    internal static string GetEmbeddedDefault()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("AchievementOverlay.default.json");
        if (stream == null)
        {
            // Fallback hardcoded default if resource not found (e.g. in tests)
            return JsonSerializer.Serialize(new SettingsData(), JsonOptions);
        }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
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
    [JsonPropertyName("gseSavesPath")]
    public string GseSavesPath { get; set; } = "";

    [JsonPropertyName("gamesPaths")]
    public string GamesPaths { get; set; } = "";

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
