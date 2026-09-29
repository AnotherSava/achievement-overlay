using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // A hand-edited null in a setting that needs a value is an invalid config, reported with the
        // key's name, rather than a null reaching code that the nullable annotations promise never sees one.
        RespectNullableAnnotations = true
    };

    /// <summary>The config file a default-constructed <see cref="AppConfig"/> reads, next to the executable.</summary>
    public static string ConfigFilePath => SettingsPath;

    private DateTime _lastWriteTimeUtc;

    /// <summary>
    /// The write time of the file version whose failure to reload was last logged, or null when none
    /// has failed. <see cref="Reload"/> retries a failing file on every property read, so this is what
    /// keeps one bad edit to one warning.
    /// </summary>
    private DateTime? _reportedFailureWriteTimeUtc;

    private SettingsData _settings = null!;
    private readonly Lock _lock = new();
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

    public IReadOnlyList<string> GamesPaths { get { Reload(); return _gamesPaths ??= ParsePathSetting("gamesPaths", _settings.GamesPaths); } }
    public IReadOnlyList<string> GseSavesPaths { get { Reload(); return _gseSavesPaths ??= ParsePathSetting("gseSavesPaths", _settings.GseSavesPaths); } }
    public string Language { get { Reload(); return _settings.Language; } }
    public bool SoundEnabled { get { Reload(); return _settings.SoundEnabled; } }
    public string SoundPath { get { Reload(); return _settings.SoundPath; } }
    public int DisplayDuration { get { Reload(); return _settings.DisplayDuration; } }
    public bool UseGameOverlaySettings { get { Reload(); return _settings.UseGameOverlaySettings; } }
    public string RecentAchievementsShortcut { get { Reload(); return _settings.RecentAchievementsShortcut; } }
    public int RecentAchievementsCount { get { Reload(); return _settings.RecentAchievementsCount; } }
    public string? SteamWebApiKey { get { Reload(); return _settings.SteamWebApiKey; } }
    public string? FirecrawlApiKey { get { Reload(); return _settings.FirecrawlApiKey; } }

    private IReadOnlyList<string>? _gseSavesPaths;
    private IReadOnlyList<string>? _gamesPaths;

    public SettingsData GetCurrent()
    {
        Reload();
        return _settings;
    }

    /// <summary>Writes one setting through <see cref="UpdateConfigValues(IReadOnlyDictionary{string, object})"/>.</summary>
    /// <exception cref="ConfigSaveException">The value was not saved.</exception>
    public void UpdateConfigValue(string propertyName, object value) => UpdateConfigValue(propertyName, value, _settingsFilePath);

    internal void UpdateConfigValue(string propertyName, object value, string settingsPath) => UpdateConfigValues(new Dictionary<string, object?> { [propertyName] = value }, settingsPath);

    /// <summary>
    /// Writes several settings in one read-modify-write pass, keyed by <see cref="SettingsData"/>
    /// property name. The settings dialog saves through here so a save is one file write rather than
    /// one per field — every write bumps the file's timestamp and triggers a reload. The file is
    /// replaced whole rather than written in place; <see cref="WriteReplacing"/> says why.
    /// </summary>
    /// <exception cref="ConfigSaveException">Nothing was saved, and the in-memory settings are unchanged.</exception>
    public void UpdateConfigValues(IReadOnlyDictionary<string, object?> values) => UpdateConfigValues(values, _settingsFilePath);

    internal void UpdateConfigValues(IReadOnlyDictionary<string, object?> values, string settingsPath)
    {
        lock (_lock)
        {
            string json;
            try
            {
                json = File.ReadAllText(settingsPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new ConfigSaveException($"Could not read the config file: {ex.Message}", ex);
            }

            Dictionary<string, JsonElement> dict;
            try
            {
                dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions) ?? new();
            }
            catch (JsonException ex)
            {
                // Writing over it would replace whatever the file holds with only the values being saved.
                throw new ConfigSaveException($"The config file is not valid JSON, so it was left as it is: {DescribeLoadError(ex)}", ex);
            }

            foreach (var (propertyName, value) in values)
            {
                var camelKey = JsonNamingPolicy.CamelCase.ConvertName(propertyName);
                dict[camelKey] = JsonSerializer.SerializeToElement(value, JsonOptions);
            }

            var updated = JsonSerializer.Serialize(dict, JsonOptions);
            try
            {
                WriteReplacing(settingsPath, updated);
                _lastWriteTimeUtc = File.GetLastWriteTimeUtc(settingsPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Thrown before the new values reach memory, where they would pass for settings the file holds.
                throw new ConfigSaveException($"Could not write the config file: {ex.Message}", ex);
            }
            try
            {
                _settings = Deserialize(updated);
                InvalidateCaches();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                Logger.Warn($"Config written, but it no longer loads; keeping the last good settings: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Writes <paramref name="contents"/> to a temporary file beside <paramref name="path"/> and swaps it in, so the
    /// name never holds a cut-off version. Writing in place truncates the file first, so a write
    /// refused partway — a full disk, a crash, another program's lock on part of the file — leaves a cut-off config
    /// that the next start cannot load, while memory still holds the settings it replaced. A failed save can leave the
    /// temporary file behind; the next save writes over it.
    /// </summary>
    /// <remarks>
    /// The swap is <see cref="File.Replace(string, string, string)"/> rather than a replacing
    /// <see cref="File.Move(string, string, bool)"/>. It is the call Microsoft names for replacing a document-like file
    /// whole, it keeps the file's own permissions, attributes and creation time, and it still succeeds while another
    /// program reads the file with deletion shared, where the move fails whenever any other handle has the file open.
    /// Neither gets past a reader that does not share deletion, which is how <see cref="Reload"/> reads; the two never
    /// overlap, because both run under <see cref="_lock"/>. The swap needs permission to delete the file and to create
    /// one in its folder, not to write the file: a deny on writing, which stops an in-place write, does not stop it,
    /// and ReplaceFile carries the deny onto the new file.
    /// </remarks>
    private static void WriteReplacing(string path, string contents)
    {
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(Encoding.UTF8.GetBytes(contents));
            // On disk before the swap, so a power loss just after it cannot leave the name on unwritten data.
            stream.Flush(flushToDisk: true);
        }

        File.Replace(temporary, path, destinationBackupFileName: null);
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
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // A bad, locked or mid-edit file keeps the last good settings rather than escaping every getter.
                // _lastWriteTimeUtc stays behind so the next read retries, which is why the warning is keyed to the write time.
                if (_reportedFailureWriteTimeUtc != currentWriteTime)
                {
                    _reportedFailureWriteTimeUtc = currentWriteTime;
                    Logger.Warn($"Config file '{filePath}' could not be reloaded; keeping the last good settings: {ex.Message}");
                }
                return;
            }

            _lastWriteTimeUtc = currentWriteTime;
            InvalidateCaches();
        }
    }

    /// <summary>
    /// A load failure as the startup dialog shows it: the parser's first sentence and the line it
    /// failed on. The rest of the message is a path and byte offsets meant for a debugger.
    /// </summary>
    public static string DescribeLoadError(JsonException ex)
    {
        var end = ex.Message.IndexOf(". ", StringComparison.Ordinal);
        var sentence = end >= 0 ? ex.Message[..(end + 1)] : ex.Message;
        return ex.LineNumber is { } line ? $"{sentence} (line {line + 1})" : sentence;
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
        // Every reader of these parses each entry as a folder, so one that cannot be parsed is refused
        // here by name, rather than thrown from whichever read meets it first.
        foreach (var (key, value) in new[] { ("gamesPaths", settings.GamesPaths), ("gseSavesPaths", settings.GseSavesPaths) })
            errors.AddRange(SplitRawPaths(value).Where(entry => !NamesAFolder(entry)).Select(entry => $"'{key}' entry '{entry}' does not name a folder"));
        if (errors.Count > 0)
            throw new InvalidOperationException("Invalid config: " + string.Join("\n", errors));
    }

    /// <summary>Whether <paramref name="entry"/>, once expanded, is a path <see cref="FolderPath.Parse"/> reads — a variable holding only spaces is not.</summary>
    private static bool NamesAFolder(string entry)
    {
        try
        {
            _ = FolderPath.Parse(ExpandEnvironmentVariables(entry));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static string ExpandEnvironmentVariables(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        return Environment.ExpandEnvironmentVariables(path);
    }

    /// <summary>
    /// Folder variables an absolute path is packed back into. The deepest one holding it wins — by
    /// folder names for a path, by expansion length in text — so a nested one (%localappdata%) wins
    /// over the parent it sits under (%userprofile%).
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
    /// <remarks>
    /// The part below the variable is written in <see cref="FolderPath"/>'s one spelling — backslashes,
    /// no trailing separator — so <c>C:/Users/Sam/AppData/Roaming/GSE Saves/</c> collapses to the same
    /// <c>%appdata%\GSE Saves</c> as the picker's own spelling does.
    /// </remarks>
    public static string CollapseEnvironmentVariables(string path)
    {
        // A relative path names a folder only against the working directory it is read from.
        if (!Path.IsPathFullyQualified(path))
            return path;

        var folder = FolderPath.Parse(path);
        string? bestVariable = null;
        FolderPath? best = null;

        foreach (var variable in CollapsibleVariables)
        {
            // An undefined variable expands to itself, which is not a folder at all.
            var expanded = ExpandEnvironmentVariables(variable);
            if (!Path.IsPathFullyQualified(expanded))
                continue;

            var candidate = FolderPath.Parse(expanded);
            if (candidate.Contains(folder) && (best == null || candidate.Names.Count > best.Names.Count))
            {
                bestVariable = variable;
                best = candidate;
            }
        }

        return best == null ? path : Path.Join(bestVariable, string.Join(Path.DirectorySeparatorChar, folder.Names.Skip(best.Names.Count)));
    }

    /// <summary>
    /// The same substitution applied to every known folder <em>inside</em> a longer string, for text
    /// that is not itself a path — a log line, a message quoting a file. A diagnostic report runs its
    /// log through this so the Windows account name stops riding along inside
    /// <c>C:\Users\Sam\AppData\Roaming\GSE Saves</c>, which is a fact about the person rather than
    /// about the problem. Longest expansion first, so <c>%appdata%</c> claims a path before
    /// <c>%userprofile%</c> can take the front of it. Each folder is matched by
    /// <see cref="ReplaceFolderInText"/>, so a longer name beside it is left as written — including a
    /// folder beside the profile whose name continues the account's with a letter, digit, <c>_</c> or
    /// <c>-</c> (<c>C:\Users\Sam_old</c>), which keeps the account name.
    /// </summary>
    public static string CollapseEnvironmentVariablesInText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var byDepth = CollapsibleVariables
            .Select(variable => (Variable: variable, Expanded: Path.TrimEndingDirectorySeparator(ExpandEnvironmentVariables(variable))))
            .Where(pair => pair.Expanded != pair.Variable && !string.IsNullOrEmpty(pair.Expanded))
            .OrderByDescending(pair => pair.Expanded.Length);

        foreach (var (variable, expanded) in byDepth)
            text = ReplaceFolderInText(text, expanded, variable);

        return text;
    }

    /// <summary>
    /// Replaces <paramref name="folder"/> wherever free text writes it — a log line, a message quoting
    /// a file — in any case and with a run of either separator at each separator: Windows reads both, a
    /// hand-edited config or a third-party file can carry <c>C:/Users/Sam</c>, and a report quoting a
    /// file as raw JSON writes <c>C:\\Users\\Sam</c>. Only where the folder's
    /// last name ends: neither <c>C:\Users\Samantha</c> nor <c>GSE Saves\8121400</c> is the folder that
    /// is a prefix of it. A name running on after a space or a dot is still taken for the folder, since
    /// nothing in free text says where a path stops.
    /// </summary>
    internal static string ReplaceFolderInText(string text, string folder, string replacement)
    {
        var pattern = string.Join(@"[\\/]+", folder.Split('\\', '/').Select(Regex.Escape)) + @"(?![\w-])";
        return Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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
    /// The same list, expanded and ready to use, without the entries <see cref="PairWithSameFolder"/>
    /// finds naming an earlier entry's folder. Splits through <see cref="SplitRawPaths"/> so the ';'
    /// convention is stated once — the raw and expanded readings can't disagree about it.
    /// </summary>
    public static string[] ParseGamesPaths(string? gamesPaths) =>
        PairWithSameFolder(SplitRawPaths(gamesPaths))
            .Where(pair => pair.SameFolderAs == null)
            .Select(pair => ExpandEnvironmentVariables(pair.Entry))
            .Where(p => !string.IsNullOrEmpty(p))
            .ToArray();

    /// <summary>
    /// Pairs each raw entry of a path setting with the earlier entry naming the same folder, which it
    /// is skipped for, or with null when it is the first to name its folder. Only the same folder: an
    /// entry inside another stays, because a games root covers the folders below it while a GSE Saves
    /// path holds only the appid folders directly inside it.
    /// </summary>
    public static IEnumerable<(string Entry, string? SameFolderAs)> PairWithSameFolder(IReadOnlyList<string> entries) =>
        entries.Select((entry, i) => (entry, FindSameFolder(entries.Take(i), entry)));

    /// <summary>
    /// The first of <paramref name="entries"/> naming the same folder as <paramref name="entry"/>, all
    /// raw as config writes them, or null when none does. They are compared as <see cref="FolderPath"/>s
    /// once expanded, so <c>C:/Games</c>, <c>C:\Games\</c> and <c>c:\games</c> are one folder, and
    /// <c>%appdata%\GSE Saves</c> is the folder it expands to.
    /// </summary>
    public static string? FindSameFolder(IEnumerable<string> entries, string entry)
    {
        var folder = FolderPath.Parse(ExpandEnvironmentVariables(entry));
        return entries.FirstOrDefault(other => FolderPath.Parse(ExpandEnvironmentVariables(other)).IsSameFolder(folder));
    }

    /// <summary>
    /// <see cref="ParseGamesPaths"/> of the setting stored under <paramref name="key"/>, warning about
    /// each entry it skips. The getters keep the result until the file is reloaded or saved, so one such
    /// entry is one warning per version of the file rather than one per read.
    /// </summary>
    private static string[] ParsePathSetting(string key, string? value)
    {
        foreach (var (entry, sameFolderAs) in PairWithSameFolder(SplitRawPaths(value)))
        {
            if (sameFolderAs != null)
                Logger.Warn($"Remove '{entry}' from '{key}' — it names the same folder as '{sameFolderAs}', so it is skipped.");
        }

        return ParseGamesPaths(value);
    }

    // --- Registry auto-start ---

    private const string RegistryRunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "AchievementOverlay";

    public static bool IsStartWithWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, false);
        return key?.GetValue(AppName) != null;
    }

    /// <exception cref="InvalidOperationException">The startup key could not be opened, or the app's own path is not available to register.</exception>
    public static void SetStartWithWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, true) ?? throw new InvalidOperationException($@"The Windows startup key 'HKEY_CURRENT_USER\{RegistryRunKey}' could not be opened.");

        if (enabled)
        {
            var exePath = Environment.ProcessPath ?? throw new InvalidOperationException("The path of this app's executable is not available, so it cannot be registered to start with Windows.");
            key.SetValue(AppName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(AppName, false);
        }
    }
}

/// <summary>A settings change that did not reach config.json, so the in-memory settings were left as they were.</summary>
public sealed class ConfigSaveException : Exception
{
    public ConfigSaveException() { }

    public ConfigSaveException(string message) : base(message) { }

    public ConfigSaveException(string message, Exception innerException) : base(message, innerException) { }
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

    /// <summary>
    /// Which corner or edge popups appear at. Absent reads as bottom-right — the enum's member 0 —
    /// so an existing install keeps the only position the app has ever had. The converter is on the
    /// type; the name has to camel-case to the JSON key, because that is how
    /// <see cref="AppConfig.UpdateConfigValues(IReadOnlyDictionary{string, object})"/> derives it when the settings window saves.
    /// </summary>
    [JsonPropertyName("notificationPosition")]
    public NotificationAnchor NotificationPosition { get; set; }

    /// <summary>
    /// The colour behind the popup's text, alpha included. Absent, or unreadable, is the shipped
    /// <c>#DD1A1A2E</c>. The text colours are derived from it rather than configured beside it.
    /// </summary>
    [JsonPropertyName("notificationBackground")]
    public PopupBackground NotificationBackground { get; set; } = PopupBackground.Default;

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
