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

    /// <summary>
    /// The raw unlock entry, so a consumer can read display text a self-describing writer inlined
    /// there instead of looking it up in a steam_settings/ schema.
    /// </summary>
    public AchievementUnlockState? UnlockState { get; init; }
}

/// <summary>
/// Event data for a per-game folder observed under a GSE Saves path (e.g. GSE Saves/1601580/),
/// signalling that an emulator ran for that appid.
/// </summary>
public sealed class GameFolderObservedEventArgs : EventArgs
{
    public required string AppId { get; init; }

    /// <summary>
    /// Parsed unlock states when the folder was observed via its achievements.json, null when it
    /// was observed as a bare folder. Carried so the handler need not re-read a file the emulator
    /// may still hold open.
    /// </summary>
    public Dictionary<string, AchievementUnlockState>? States { get; init; }
}

/// <summary>
/// Watches the GSE Saves directory for achievements.json changes.
/// Detects new achievement unlocks by diffing against cached state
/// and raises NewAchievement events.
/// </summary>
public sealed class AchievementWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly string[] _gseSavesPaths;

    // Tracks last-seen earned_time per (appid, achievementName) to avoid duplicate notifications
    private readonly ConcurrentDictionary<string, long> _seenAchievements = new();

    // Tracks last (write-time, length) per file to skip unchanged files
    private readonly ConcurrentDictionary<string, (DateTime Time, long Length)> _lastModTimes = new();

    // Debounce: tracks pending file change callbacks
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _debounceTokens = new();

    // Appids already covered by a seeding pass. An appid absent from this set has a folder that
    // appeared after Start(), so its existing unlocks must be seeded rather than replayed.
    private readonly ConcurrentDictionary<string, byte> _seededAppIds = new();

    // Appids already announced via GameFolderObserved, so the per-file path raises at most once
    // per session. Concurrent because ProcessFileAsync runs on fire-and-forget tasks.
    private readonly ConcurrentDictionary<string, byte> _observedAppIds = new();

    // Unix time the watcher started; unlocks older than this were earned before we were watching.
    private long _startedAtUnix;

    private readonly TimeSpan _debounceDelay;
    private readonly int _maxRetries;
    private readonly TimeSpan _retryDelay;

    public event EventHandler<NewAchievementEventArgs>? NewAchievement;

    /// <summary>
    /// Raised the first time each appid is observed under a GSE Saves path — either as a newly
    /// created folder, or on the first successful read of its achievements.json. The second path
    /// matters because an emulator creates the folder before writing the file, so the folder
    /// event alone fires while there is nothing yet to judge the game by.
    /// </summary>
    public event EventHandler<GameFolderObservedEventArgs>? GameFolderObserved;

    public AchievementWatcher(
        string[] gseSavesPaths,
        TimeSpan? debounceDelay = null,
        int maxRetries = 3,
        TimeSpan? retryDelay = null)
    {
        _gseSavesPaths = gseSavesPaths;
        _debounceDelay = debounceDelay ?? TimeSpan.FromMilliseconds(100);
        _maxRetries = maxRetries;
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(200);
    }

    /// <summary>
    /// Starts watching the GSE Saves directory for achievement changes.
    /// Seeds every existing appid's achievements so they don't replay as new. Seeding is no longer
    /// filtered by the configured games: an appid that becomes resolvable later (a self-describing
    /// unlock file needs no configuration) would otherwise replay its whole backlog at once.
    /// </summary>
    public void Start()
    {
        if (_watchers.Count > 0)
            return;

        _startedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (var path in _gseSavesPaths)
        {
            SeedExistingAchievementsFromDirectory(path);

            var watcher = new FileSystemWatcher(path)
            {
                Filter = "achievements.json",
                IncludeSubdirectories = true,
                InternalBufferSize = 32768,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName
            };

            watcher.Changed += OnFileChanged;
            watcher.Created += OnFileChanged;
            // A writer that saves via temp-file-plus-rename raises Renamed, which neither of the
            // above covers — without this such an emulator's unlocks are missed silently.
            watcher.Renamed += OnFileChanged;
            watcher.Error += (_, e) => Logger.Warn($"FileSystemWatcher error: {e.GetException().Message}");
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);

            // The achievements.json filter above does not fire on bare folder creation, so a second
            // watcher tracks new top-level appid folders (the emulator's first-run signal).
            var dirWatcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.DirectoryName
            };
            dirWatcher.Created += OnDirectoryCreated;
            dirWatcher.Error += (_, e) => Logger.Warn($"FileSystemWatcher (dir) error: {e.GetException().Message}");
            dirWatcher.EnableRaisingEvents = true;
            _watchers.Add(dirWatcher);

            Logger.Info($"Watching for achievements in '{path}'");
        }
    }

    /// <summary>
    /// Returns the appid (folder names) of every top-level subdirectory across all watched
    /// GSE Saves paths. Used to detect already-existing game folders at startup.
    /// </summary>
    public IEnumerable<string> GetExistingAppIdFolders()
    {
        foreach (var path in _gseSavesPaths)
        {
            if (!Directory.Exists(path))
                continue;

            foreach (var dir in Directory.GetDirectories(path))
                yield return Path.GetFileName(dir);
        }
    }

    /// <summary>
    /// Re-seeds already-earned achievements from disk, so a newly added game's existing unlocks
    /// don't replay as notifications. Idempotent.
    /// </summary>
    public void ReseedAll()
    {
        foreach (var path in _gseSavesPaths)
            SeedExistingAchievementsFromDirectory(path);
    }

    /// <summary>
    /// Stops watching.
    /// </summary>
    public void Stop()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();

        Logger.Info("Achievement watcher stopped.");

        // Cancel and dispose any pending debounce callbacks
        foreach (var cts in _debounceTokens.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _debounceTokens.Clear();
    }

    public void Dispose() => Stop();

    private void OnDirectoryCreated(object sender, FileSystemEventArgs e)
    {
        var appId = Path.GetFileName(e.FullPath);
        if (string.IsNullOrEmpty(appId))
            return;

        Logger.Info($"New GSE Saves folder detected: appid={appId}");

        // Deliberately not guarded by _observedAppIds: a folder is created once, and at this point
        // there is usually no achievements.json yet, so the handler cannot judge the game. Burning
        // the guard here would suppress the file-based raise that finally carries the data.
        GameFolderObserved?.Invoke(this, new GameFolderObservedEventArgs { AppId = appId, States = null });
    }

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
            Logger.Info($"Could not extract appid from path: '{filePath}'");
            return;
        }

        // Check modification time — skip if unchanged
        if (!HasFileChanged(filePath))
        {
            Logger.Info($"File unchanged (mod time), skipping: '{filePath}'");
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
            Logger.Warn($"JSON parse error for '{filePath}': {ex.Message}");
            return;
        }

        // Announce the folder now that there is something to judge the game by — before the
        // unlock diff, so a setup confirmation is queued ahead of any real unlock.
        if (_observedAppIds.TryAdd(appId, 0))
            GameFolderObserved?.Invoke(this, new GameFolderObservedEventArgs { AppId = appId, States = states });

        // A folder that appeared after Start() was never seeded. Its pre-existing unlocks must be
        // recorded, not replayed — otherwise moving save files in mid-session (or a cloud sync
        // dropping a folder) fires one popup per achievement, seconds apart.
        if (_seededAppIds.TryAdd(appId, 0))
        {
            var backlog = states
                .Where(s => s.Value.Earned && s.Value.EarnedTime < _startedAtUnix)
                .ToDictionary(s => s.Key, s => s.Value);

            if (backlog.Count > 0)
            {
                SeedExistingAchievements(appId, backlog);
                Logger.Info($"Seeded {backlog.Count} pre-existing achievement(s) for newly seen appid {appId}");
            }
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

            Logger.Info($"New achievement unlocked: appid={appId}, name={achName}, time={state.EarnedTime}");

            NewAchievement?.Invoke(this, new NewAchievementEventArgs
            {
                AppId = appId,
                AchievementName = achName,
                EarnedTime = state.EarnedTime,
                UnlockState = state
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

    /// <summary>
    /// True when the file looks different from the last time it was processed. Compares length as
    /// well as last-write-time: Windows can defer the timestamp update for a file a writer still
    /// holds open, and this check hard-skips the file, so a stale timestamp would silently cost
    /// every unlock in it.
    /// </summary>
    private bool HasFileChanged(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            var stamp = (info.LastWriteTimeUtc, info.Length);

            if (_lastModTimes.TryGetValue(filePath, out var last) && stamp == last)
                return false;

            _lastModTimes[filePath] = stamp;
            return true;
        }
        catch
        {
            // If we can't check, assume changed
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
                Logger.Info($"File locked, retry {attempt + 1}/{_maxRetries}: '{filePath}'");
                await Task.Delay(_retryDelay);
            }
            catch (FileNotFoundException)
            {
                Logger.Info($"File not found (may have been deleted): '{filePath}'");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Info($"Error reading '{filePath}': {ex.Message}");
                return null;
            }
        }

        Logger.Warn($"Failed to read after {_maxRetries} retries: '{filePath}'");
        return null;
    }

    /// <summary>
    /// Scans all appid subdirectories under the GSE Saves path and seeds existing
    /// achievements so they don't replay as new notifications.
    /// </summary>
    private void SeedExistingAchievementsFromDirectory(string gseSavesPath)
    {
        if (!Directory.Exists(gseSavesPath))
            return;

        foreach (var dir in Directory.GetDirectories(gseSavesPath))
        {
            var appId = Path.GetFileName(dir);
            _seededAppIds.TryAdd(appId, 0);

            var achievementsFile = Path.Combine(dir, "achievements.json");
            if (!File.Exists(achievementsFile))
                continue;

            try
            {
                var json = File.ReadAllText(achievementsFile);
                var states = AchievementMetadata.ParseUnlockStates(json);
                SeedExistingAchievements(appId, states);
                Logger.Info($"Seeded {states.Count(s => s.Value.Earned)} existing achievement(s) for appid {appId}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Error seeding achievements for appid {appId}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Seeds the cache with already-earned achievements so they don't fire as new.
    /// Call this after initial scan to avoid replaying old unlocks.
    /// Records only achievements not already observed — a re-seed running concurrently with a
    /// live unlock must not overwrite (and thereby swallow) what the watcher has just seen.
    /// </summary>
    public void SeedExistingAchievements(string appId, Dictionary<string, AchievementUnlockState> states)
    {
        foreach (var (achName, state) in states)
        {
            if (state.Earned)
            {
                var key = $"{appId}|{achName}";
                _seenAchievements.TryAdd(key, state.EarnedTime);
            }
        }
    }
}
