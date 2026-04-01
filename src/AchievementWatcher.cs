using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace AchievementOverlay;

/// <summary>
/// Event data for a newly unlocked achievement.
/// </summary>
public sealed class NewAchievementEventArgs : EventArgs
{
    public required string AppId { get; init; }
    public required string AchievementName { get; init; }
    public required long EarnedTime { get; init; }
}

/// <summary>
/// Watches the GSE Saves directory for achievements.json changes.
/// Detects new achievement unlocks by diffing against cached state
/// and raises NewAchievement events.
/// </summary>
public sealed class AchievementWatcher : IDisposable
{
    private FileSystemWatcher? _watcher;
    private readonly string _gseSavesPath;
    private readonly Action<string>? _log;

    // Tracks last-seen earned_time per (appid, achievementName) to avoid duplicate notifications
    private readonly ConcurrentDictionary<string, long> _seenAchievements = new();

    // Tracks last modification time per file to skip unchanged files
    private readonly ConcurrentDictionary<string, DateTime> _lastModTimes = new();

    // Debounce: tracks pending file change callbacks
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _debounceTokens = new();

    private readonly TimeSpan _debounceDelay;
    private readonly int _maxRetries;
    private readonly TimeSpan _retryDelay;

    public event EventHandler<NewAchievementEventArgs>? NewAchievement;

    public AchievementWatcher(
        string gseSavesPath,
        Action<string>? log = null,
        TimeSpan? debounceDelay = null,
        int maxRetries = 3,
        TimeSpan? retryDelay = null)
    {
        _gseSavesPath = gseSavesPath;
        _log = log;
        _debounceDelay = debounceDelay ?? TimeSpan.FromMilliseconds(100);
        _maxRetries = maxRetries;
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(200);
    }

    public string GseSavesPath => _gseSavesPath;

    /// <summary>
    /// Starts watching the GSE Saves directory for achievement changes.
    /// Seeds existing achievements for the given appids to avoid replaying old unlocks.
    /// </summary>
    public void Start(IEnumerable<string>? knownAppIds = null)
    {
        if (_watcher != null)
            return;

        SeedExistingAchievementsFromDirectory(_gseSavesPath, knownAppIds);

        if (!Directory.Exists(_gseSavesPath))
        {
            _log?.Invoke($"GSE Saves path does not exist, creating: '{_gseSavesPath}'");
            Directory.CreateDirectory(_gseSavesPath);
        }

        _watcher = new FileSystemWatcher(_gseSavesPath)
        {
            Filter = "achievements.json",
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName
        };

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.EnableRaisingEvents = true;

        _log?.Invoke($"Watching for achievements in '{_gseSavesPath}'");
    }

    /// <summary>
    /// Stops watching.
    /// </summary>
    public void Stop()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        _log?.Invoke("Achievement watcher stopped.");

        // Cancel and dispose any pending debounce callbacks
        foreach (var cts in _debounceTokens.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _debounceTokens.Clear();
    }

    public void Dispose() => Stop();

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        var filePath = e.FullPath;

        // Cancel any previous pending debounce for this file
        if (_debounceTokens.TryRemove(filePath, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }

        var cts = new CancellationTokenSource();
        _debounceTokens[filePath] = cts;

        _ = DebounceAndProcessAsync(filePath, cts);
    }

    private async Task DebounceAndProcessAsync(string filePath, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(_debounceDelay, cts.Token);
        }
        catch (TaskCanceledException)
        {
            return; // Superseded by a newer change event
        }

        // Only process if this CTS is still the active one for this file
        if (_debounceTokens.TryRemove(filePath, out var current))
        {
            if (!ReferenceEquals(current, cts))
            {
                // A newer event replaced us — put its CTS back and bail out
                _debounceTokens.TryAdd(filePath, current);
                cts.Dispose();
                return;
            }
        }
        cts.Dispose();
        await ProcessFileAsync(filePath);
    }

    /// <summary>
    /// Synchronous wrapper for testing — calls ProcessFileAsync synchronously.
    /// </summary>
    internal void ProcessFile(string filePath)
    {
        ProcessFileAsync(filePath).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Processes an achievements.json file, detecting new unlocks.
    /// </summary>
    internal async Task ProcessFileAsync(string filePath)
    {
        // Extract appid from path: <gseSavesPath>/<appid>/achievements.json
        var appId = ExtractAppId(filePath);
        if (appId == null)
        {
            _log?.Invoke($"Could not extract appid from path: '{filePath}'");
            return;
        }

        // Check modification time — skip if unchanged
        if (!HasFileChanged(filePath))
        {
            _log?.Invoke($"File unchanged (mod time), skipping: '{filePath}'");
            return;
        }

        // Read file with retry for locked files
        string? json = await ReadFileWithRetryAsync(filePath);
        if (json == null)
            return;

        // Parse unlock states
        Dictionary<string, AchievementUnlockState> states;
        try
        {
            states = AchievementMetadata.ParseUnlockStates(json);
        }
        catch (JsonException ex)
        {
            _log?.Invoke($"JSON parse error for '{filePath}': {ex.Message}");
            return;
        }

        // Diff against cached state to find new unlocks
        foreach (var (achName, state) in states)
        {
            if (!state.Earned)
                continue;

            var key = $"{appId}|{achName}";
            var earnedTime = state.EarnedTime;

            // Atomically add or check: if key already present with same time, skip.
            // TryAdd returns false if key exists; then verify the existing value matches.
            if (!_seenAchievements.TryAdd(key, earnedTime))
            {
                if (_seenAchievements.TryGetValue(key, out var prev) && prev == earnedTime)
                    continue;
                _seenAchievements[key] = earnedTime;
            }

            _log?.Invoke($"New achievement unlocked: appid={appId}, name={achName}, time={state.EarnedTime}");

            NewAchievement?.Invoke(this, new NewAchievementEventArgs
            {
                AppId = appId,
                AchievementName = achName,
                EarnedTime = state.EarnedTime
            });
        }
    }

    /// <summary>
    /// Extracts the appid from a file path like .../GSE Saves/12345/achievements.json.
    /// </summary>
    internal static string? ExtractAppId(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (dir == null)
            return null;

        return Path.GetFileName(dir);
    }

    private bool HasFileChanged(string filePath)
    {
        try
        {
            var modTime = File.GetLastWriteTimeUtc(filePath);
            var key = filePath;

            if (_lastModTimes.TryGetValue(key, out var lastMod) && modTime == lastMod)
                return false;

            _lastModTimes[key] = modTime;
            return true;
        }
        catch
        {
            // If we can't check mod time, assume changed
            return true;
        }
    }

    private async Task<string?> ReadFileWithRetryAsync(string filePath)
    {
        for (var attempt = 0; attempt <= _maxRetries; attempt++)
        {
            try
            {
                return await File.ReadAllTextAsync(filePath);
            }
            catch (IOException) when (attempt < _maxRetries)
            {
                _log?.Invoke($"File locked, retry {attempt + 1}/{_maxRetries}: '{filePath}'");
                await Task.Delay(_retryDelay);
            }
            catch (FileNotFoundException)
            {
                _log?.Invoke($"File not found (may have been deleted): '{filePath}'");
                return null;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Error reading '{filePath}': {ex.Message}");
                return null;
            }
        }

        _log?.Invoke($"Failed to read after {_maxRetries} retries: '{filePath}'");
        return null;
    }

    /// <summary>
    /// Scans all appid subdirectories under the GSE Saves path and seeds existing
    /// achievements so they don't replay as new notifications.
    /// </summary>
    private void SeedExistingAchievementsFromDirectory(string gseSavesPath, IEnumerable<string>? knownAppIds)
    {
        if (!Directory.Exists(gseSavesPath))
            return;

        var filter = knownAppIds != null ? new HashSet<string>(knownAppIds) : null;

        foreach (var dir in Directory.GetDirectories(gseSavesPath))
        {
            var achievementsFile = Path.Combine(dir, "achievements.json");
            if (!File.Exists(achievementsFile))
                continue;

            var appId = Path.GetFileName(dir);
            if (filter != null && !filter.Contains(appId))
                continue;
            try
            {
                var json = File.ReadAllText(achievementsFile);
                var states = AchievementMetadata.ParseUnlockStates(json);
                SeedExistingAchievements(appId, states);
                _log?.Invoke($"Seeded {states.Count(s => s.Value.Earned)} existing achievement(s) for appid {appId}");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Error seeding achievements for appid {appId}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Seeds the cache with already-earned achievements so they don't fire as new.
    /// Call this after initial scan to avoid replaying old unlocks.
    /// </summary>
    public void SeedExistingAchievements(string appId, Dictionary<string, AchievementUnlockState> states)
    {
        foreach (var (achName, state) in states)
        {
            if (state.Earned)
            {
                var key = $"{appId}|{achName}";
                _seenAchievements[key] = state.EarnedTime;
            }
        }
    }
}
