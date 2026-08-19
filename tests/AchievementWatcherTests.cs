using System.IO;
using AchievementOverlay;
using Xunit;

namespace AchievementOverlay.Tests;

public class AchievementWatcherTests : IDisposable
{
    private readonly string _tempDir;
    private readonly List<NewAchievementEventArgs> _events = new();
    private readonly object _eventsLock = new();

    public AchievementWatcherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AchievementWatcherTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string CreateAppDir(string appId)
    {
        var dir = Path.Combine(_tempDir, appId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private string WriteAchievementsJson(string appId, string json)
    {
        var dir = CreateAppDir(appId);
        var path = Path.Combine(dir, "achievements.json");
        WriteFile(path, json);
        return path;
    }

    /// <summary>
    /// Writes the file the way the emulator does — retrying a sharing violation instead of failing.
    /// A started watcher reads achievements.json through <c>File.ReadAllTextAsync</c>, which opens
    /// with <c>FileShare.Read</c>: concurrent readers are allowed, writers are not. So a test that
    /// writes while its own subject happens to be reading loses the race and throws
    /// "used by another process". The production reader already retries the mirror image of this
    /// (<c>ReadFileWithRetryAsync</c>); only the writing side was unguarded.
    /// </summary>
    private static void WriteFile(string path, string contents)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.WriteAllText(path, contents);
                return;
            }
            catch (IOException ex) when (attempt < 20 && ex is not (FileNotFoundException or DirectoryNotFoundException))
            {
                Thread.Sleep(10);
            }
        }
    }

    private AchievementWatcher CreateWatcher()
    {
        var watcher = new AchievementWatcher(
            new[] { _tempDir },
            // The production default. A debounce far shorter than the gap between a test's writes
            // makes "rapid changes" collapse only by luck, since Task.Delay is a floor rather than
            // a deadline and Windows' timer granularity is coarser than the margin.
            debounceDelay: TimeSpan.FromMilliseconds(100),
            maxRetries: 2,
            retryDelay: TimeSpan.FromMilliseconds(10));
        // Raised from the watcher's fire-and-forget processing tasks, so adds are serialised here;
        // assertions read after the watcher has quiesced.
        watcher.NewAchievement += (_, e) => { lock (_eventsLock) _events.Add(e); };
        return watcher;
    }

    // --- ExtractAppId tests ---

    [Fact]
    public void ExtractAppId_ValidPath_ReturnsAppId()
    {
        var path = Path.Combine("C:", "GSE Saves", "12345", "achievements.json");
        Assert.Equal("12345", AchievementWatcher.ExtractAppId(path));
    }

    [Fact]
    public void ExtractAppId_RootFile_ReturnsParentDirName()
    {
        var path = Path.Combine("some_folder", "achievements.json");
        Assert.Equal("some_folder", AchievementWatcher.ExtractAppId(path));
    }

    // --- ProcessFile: detect new unlock ---

    [Fact]
    public void ProcessFile_NewUnlock_RaisesEvent()
    {
        var json = """{"ACH01": {"earned": true, "earned_time": 1700000000}}""";
        var filePath = WriteAchievementsJson("12345", json);

        using var watcher = CreateWatcher();
        watcher.ProcessFile(filePath);

        Assert.Single(_events);
        Assert.Equal("12345", _events[0].AppId);
        Assert.Equal("ACH01", _events[0].AchievementName);
        Assert.Equal(1700000000L, _events[0].EarnedTime);
    }

    // --- ProcessFile: ignore already-seen unlock ---

    [Fact]
    public void ProcessFile_AlreadySeenUnlock_DoesNotRaiseEvent()
    {
        var json = """{"ACH01": {"earned": true, "earned_time": 1700000000}}""";
        var filePath = WriteAchievementsJson("12345", json);

        using var watcher = CreateWatcher();
        watcher.ProcessFile(filePath);
        Assert.Single(_events);

        // Process same file again — need to bump mod time for it to pass the mod time check
        Thread.Sleep(50);
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow);
        watcher.ProcessFile(filePath);

        // Still only one event — same earned_time means it's not new
        Assert.Single(_events);
    }

    // --- ProcessFile: multiple simultaneous unlocks ---

    [Fact]
    public void ProcessFile_MultipleUnlocks_RaisesMultipleEvents()
    {
        var json = """
        {
            "ACH01": {"earned": true, "earned_time": 1700000000},
            "ACH02": {"earned": true, "earned_time": 1700000001},
            "ACH03": {"earned": false, "earned_time": 0}
        }
        """;
        var filePath = WriteAchievementsJson("12345", json);

        using var watcher = CreateWatcher();
        watcher.ProcessFile(filePath);

        // ACH03 is not earned, so only 2 events
        Assert.Equal(2, _events.Count);
        Assert.Contains(_events, e => e.AchievementName == "ACH01");
        Assert.Contains(_events, e => e.AchievementName == "ACH02");
        Assert.DoesNotContain(_events, e => e.AchievementName == "ACH03");
    }

    // --- ProcessFile: modification time check skips unchanged files ---

    [Fact]
    public void ProcessFile_UnchangedModTime_SkipsFile()
    {
        var json = """{"ACH01": {"earned": true, "earned_time": 1700000000}}""";
        var filePath = WriteAchievementsJson("12345", json);

        using var watcher = CreateWatcher();
        watcher.ProcessFile(filePath);
        Assert.Single(_events);

        // Process again without changing mod time — should skip entirely
        watcher.ProcessFile(filePath);
        Assert.Single(_events); // No new events
    }

    // --- ProcessFile: seeded achievements don't fire ---

    [Fact]
    public void SeedExistingAchievements_PreventsNotification()
    {
        var json = """{"ACH01": {"earned": true, "earned_time": 1700000000}}""";
        var filePath = WriteAchievementsJson("12345", json);

        using var watcher = CreateWatcher();

        // Seed the existing state before processing
        var states = AchievementMetadata.ParseUnlockStates(json);
        watcher.SeedExistingAchievements("12345", states);

        watcher.ProcessFile(filePath);

        // No events because ACH01 was seeded
        Assert.Empty(_events);
    }

    // --- ProcessFile: new unlock after seeding ---

    [Fact]
    public void ProcessFile_NewUnlockAfterSeeding_RaisesEvent()
    {
        // Seed with ACH01 only
        var seedJson = """{"ACH01": {"earned": true, "earned_time": 1700000000}}""";
        using var watcher = CreateWatcher();
        var states = AchievementMetadata.ParseUnlockStates(seedJson);
        watcher.SeedExistingAchievements("12345", states);

        // Now file has ACH01 + ACH02
        var json = """
        {
            "ACH01": {"earned": true, "earned_time": 1700000000},
            "ACH02": {"earned": true, "earned_time": 1700000100}
        }
        """;
        var filePath = WriteAchievementsJson("12345", json);
        watcher.ProcessFile(filePath);

        // Only ACH02 is new
        Assert.Single(_events);
        Assert.Equal("ACH02", _events[0].AchievementName);
    }

    // --- Seeding never overwrites an already-observed unlock ---

    [Fact]
    public void SeedExistingAchievements_DoesNotOverwriteObservedUnlock()
    {
        var json = """{"ACH01": {"earned": true, "earned_time": 1700000000}}""";
        var filePath = WriteAchievementsJson("12345", json);

        using var watcher = CreateWatcher();
        watcher.ProcessFile(filePath);
        Assert.Single(_events);

        // A re-seed (e.g. after Add game) racing a fresh unlock must not record the new
        // earned_time as "already seen" — that would swallow the notification silently.
        var reEarned = """{"ACH01": {"earned": true, "earned_time": 1700000999}}""";
        watcher.SeedExistingAchievements("12345", AchievementMetadata.ParseUnlockStates(reEarned));

        Thread.Sleep(50);
        WriteFile(filePath, reEarned);
        watcher.ProcessFile(filePath);

        Assert.Equal(2, _events.Count);
        Assert.Equal(1700000999L, _events[1].EarnedTime);
    }

    // --- ProcessFile: JSON parse error ---

    [Fact]
    public void ProcessFile_InvalidJson_LogsErrorAndSkips()
    {
        var dir = CreateAppDir("12345");
        var filePath = Path.Combine(dir, "achievements.json");
        WriteFile(filePath, "not valid json {{{");

        using var watcher = CreateWatcher();
        watcher.ProcessFile(filePath);

        Assert.Empty(_events);
    }

    // --- ProcessFile: file not found ---

    [Fact]
    public void ProcessFile_FileNotFound_LogsAndSkips()
    {
        var filePath = Path.Combine(_tempDir, "99999", "achievements.json");

        using var watcher = CreateWatcher();
        watcher.ProcessFile(filePath);

        Assert.Empty(_events);
    }

    // --- ProcessFile: changed earned_time triggers re-notification ---

    [Fact]
    public void ProcessFile_ChangedEarnedTime_RaisesNewEvent()
    {
        var json1 = """{"ACH01": {"earned": true, "earned_time": 1700000000}}""";
        var filePath = WriteAchievementsJson("12345", json1);

        using var watcher = CreateWatcher();
        watcher.ProcessFile(filePath);
        Assert.Single(_events);

        // Update with a new earned_time (re-earned)
        Thread.Sleep(50);
        var json2 = """{"ACH01": {"earned": true, "earned_time": 1700000999}}""";
        WriteFile(filePath, json2);
        watcher.ProcessFile(filePath);

        Assert.Equal(2, _events.Count);
        Assert.Equal(1700000999L, _events[1].EarnedTime);
    }

    // --- Start/Stop ---

    [Fact]
    public void Start_CreatesWatcherOnDirectory()
    {
        using var watcher = CreateWatcher();
        watcher.Start();
        // Should not throw; watcher is now active
    }

    [Fact]
    public void Stop_AfterStart_LogsStop()
    {
        using var watcher = CreateWatcher();
        watcher.Start();
        watcher.Stop();
        // Should not throw; watcher is stopped
    }

    [Fact]
    public void Start_NonExistentPath_Throws()
    {
        var nonExistent = Path.Combine(_tempDir, "new_saves_dir");
        var watcher = new AchievementWatcher(new[] { nonExistent });
        Assert.Throws<ArgumentException>(() => watcher.Start());
        watcher.Dispose();
    }

    // --- FileSystemWatcher integration test ---

    [Fact]
    public async Task FileChange_TriggersProcessingViaWatcher()
    {
        var appDir = CreateAppDir("77777");

        using var watcher = CreateWatcher();
        watcher.Start();

        // Write achievements file — the watcher should pick it up
        var filePath = Path.Combine(appDir, "achievements.json");
        var json = """{"ACH01": {"earned": true, "earned_time": 1700000000}}""";
        WriteFile(filePath, json);

        // Wait for debounce + processing
        await Task.Delay(500);

        Assert.Single(_events);
        Assert.Equal("77777", _events[0].AppId);
        Assert.Equal("ACH01", _events[0].AchievementName);
    }

    /// <summary>
    /// Two unlocks written back to back are each reported once. Whether the debounce actually
    /// collapsed them into one pass is deliberately not asserted: the counts are identical either
    /// way — one pass sees both as new, two passes see one each — and the only way to tell them
    /// apart would be a pass counter on the watcher that exists solely for this test.
    /// </summary>
    [Fact]
    public async Task RapidChanges_ReportEachUnlockExactlyOnce()
    {
        var appDir = CreateAppDir("88888");
        var filePath = Path.Combine(appDir, "achievements.json");

        using var watcher = CreateWatcher();
        watcher.Start();

        WriteFile(filePath, """{"ACH01": {"earned": true, "earned_time": 1700000000}}""");
        await Task.Delay(5);
        WriteFile(filePath, """{"ACH01": {"earned": true, "earned_time": 1700000000}, "ACH02": {"earned": true, "earned_time": 1700000001}}""");

        // Wait for debounce + processing
        await Task.Delay(500);

        // No duplicates: a repeated pass over the same earned_time is dropped by the seen-unlock map.
        Assert.Equal(2, _events.Count);
        Assert.Contains(_events, e => e.AchievementName == "ACH01");
        Assert.Contains(_events, e => e.AchievementName == "ACH02");
    }

    // --- Self-describing unlock files (issue #5) ---

    [Fact]
    public void ProcessFile_UplayFormat_RaisesEventCarryingUnlockState()
    {
        var json = """
        {
          "AFOP_Ach_7": {"earned": 0, "displayName": "First Strike", "description": "Complete the quest Becoming."},
          "AFOP_Ach_8": {"earned": 1, "earned_time": 1785988975, "displayName": "Homecoming", "description": "Reach the Hometree."}
        }
        """;
        var filePath = WriteAchievementsJson("2840770", json);

        using var watcher = CreateWatcher();
        watcher.ProcessFile(filePath);

        // Only the unlocked one fires, and it carries the inline text for the consumer to resolve.
        Assert.Single(_events);
        Assert.Equal("AFOP_Ach_8", _events[0].AchievementName);
        Assert.Equal(1785988975L, _events[0].EarnedTime);
        Assert.NotNull(_events[0].UnlockState);
        Assert.True(AchievementMetadata.HasInlineText(_events[0].UnlockState));
    }

    [Fact]
    public void ProcessFile_FolderAppearingAfterStart_SeedsBacklogInsteadOfReplaying()
    {
        using var watcher = CreateWatcher();
        watcher.Start();

        // A folder dropped in mid-session (save migration, cloud sync) full of old unlocks.
        var json = """
        {
          "ACH01": {"earned": 1, "earned_time": 1700000000, "displayName": "One", "description": "d"},
          "ACH02": {"earned": 1, "earned_time": 1700000001, "displayName": "Two", "description": "d"}
        }
        """;
        var filePath = WriteAchievementsJson("2840770", json);
        watcher.ProcessFile(filePath);

        Assert.Empty(_events);
    }

    [Fact]
    public void ProcessFile_UnlockAfterFolderAppeared_StillNotifies()
    {
        using var watcher = CreateWatcher();
        watcher.Start();

        var filePath = WriteAchievementsJson("2840770",
            """{"ACH01": {"earned": 1, "earned_time": 1700000000, "displayName": "One", "description": "d"}}""");
        watcher.ProcessFile(filePath);
        Assert.Empty(_events);

        // A genuinely new unlock — earned now, not before the watcher started.
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 5;
        Thread.Sleep(50);
        WriteFile(filePath,
            """{"ACH01": {"earned": 1, "earned_time": 1700000000, "displayName": "One", "description": "d"}, "ACH02": {"earned": 1, "earned_time": """
            + now + """, "displayName": "Two", "description": "d"}}""");
        watcher.ProcessFile(filePath);

        Assert.Single(_events);
        Assert.Equal("ACH02", _events[0].AchievementName);
    }

    [Fact]
    public void GameFolderObserved_RaisedOnceFromFileRead_CarryingStates()
    {
        var observed = new List<GameFolderObservedEventArgs>();
        var filePath = WriteAchievementsJson("2840770",
            """{"ACH01": {"earned": 0, "displayName": "One", "description": "d"}}""");

        using var watcher = CreateWatcher();
        watcher.GameFolderObserved += (_, e) => observed.Add(e);

        watcher.ProcessFile(filePath);
        Thread.Sleep(50);
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow);
        watcher.ProcessFile(filePath);

        Assert.Single(observed);
        Assert.Equal("2840770", observed[0].AppId);
        Assert.NotNull(observed[0].States);
        Assert.True(AchievementMetadata.IsSelfDescribing(observed[0].States!));
    }
}
