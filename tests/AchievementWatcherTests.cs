using System.IO;
using AchievementOverlay;
using Xunit;

namespace AchievementOverlay.Tests;

public class AchievementWatcherTests : IDisposable
{
    private readonly string _tempDir;
    private readonly List<string> _logMessages = new();
    private readonly List<NewAchievementEventArgs> _events = new();

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

    private void Log(string msg) => _logMessages.Add(msg);

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
        File.WriteAllText(path, json);
        return path;
    }

    private AchievementWatcher CreateWatcher()
    {
        var watcher = new AchievementWatcher(
            _tempDir,
            Log,
            debounceDelay: TimeSpan.FromMilliseconds(10),
            maxRetries: 2,
            retryDelay: TimeSpan.FromMilliseconds(10));
        watcher.NewAchievement += (_, e) => _events.Add(e);
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
        Assert.Contains(_logMessages, m => m.Contains("unchanged"));
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

    // --- ProcessFile: JSON parse error ---

    [Fact]
    public void ProcessFile_InvalidJson_LogsErrorAndSkips()
    {
        var dir = CreateAppDir("12345");
        var filePath = Path.Combine(dir, "achievements.json");
        File.WriteAllText(filePath, "not valid json {{{");

        using var watcher = CreateWatcher();
        watcher.ProcessFile(filePath);

        Assert.Empty(_events);
        Assert.Contains(_logMessages, m => m.Contains("JSON parse error"));
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
        File.WriteAllText(filePath, json2);
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
        // Should not throw, and log a start message
        Assert.Contains(_logMessages, m => m.Contains("Watching for achievements"));
    }

    [Fact]
    public void Stop_AfterStart_LogsStop()
    {
        using var watcher = CreateWatcher();
        watcher.Start();
        watcher.Stop();
        Assert.Contains(_logMessages, m => m.Contains("stopped"));
    }

    [Fact]
    public void Start_NonExistentPath_WarnsAndSkips()
    {
        var nonExistent = Path.Combine(_tempDir, "new_saves_dir");
        var watcher = new AchievementWatcher(nonExistent, Log, Log);
        watcher.Start();

        Assert.False(Directory.Exists(nonExistent));
        Assert.Contains(_logMessages, m => m.Contains("does not exist"));
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
        File.WriteAllText(filePath, json);

        // Wait for debounce + processing
        await Task.Delay(500);

        Assert.Single(_events);
        Assert.Equal("77777", _events[0].AppId);
        Assert.Equal("ACH01", _events[0].AchievementName);
    }

    [Fact]
    public async Task MultipleRapidChanges_DebouncesToSingleProcess()
    {
        var appDir = CreateAppDir("88888");
        var filePath = Path.Combine(appDir, "achievements.json");

        using var watcher = CreateWatcher();
        watcher.Start();

        // Rapid writes — debounce should collapse these
        File.WriteAllText(filePath, """{"ACH01": {"earned": true, "earned_time": 1700000000}}""");
        await Task.Delay(5);
        File.WriteAllText(filePath, """{"ACH01": {"earned": true, "earned_time": 1700000000}, "ACH02": {"earned": true, "earned_time": 1700000001}}""");

        // Wait for debounce + processing
        await Task.Delay(500);

        // Should have processed the final state: ACH01 + ACH02
        Assert.Contains(_events, e => e.AchievementName == "ACH01");
        Assert.Contains(_events, e => e.AchievementName == "ACH02");
    }
}
