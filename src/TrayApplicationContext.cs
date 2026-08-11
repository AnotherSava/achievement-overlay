using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using AchievementOverlay.GbeConfig;

namespace AchievementOverlay;

/// <summary>
/// WinForms application context that manages the system tray icon, context menu,
/// and wires together all components (AchievementWatcher, GameCache, NotificationQueue).
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly AppConfig _config = null!;
    private readonly GameCache _gameCache = null!;
    private readonly AchievementWatcher _watcher = null!;
    private readonly NotificationQueue _notificationQueue = null!;
    private readonly UnlockSoundPlayer _soundPlayer = null!;
    private readonly AchievementHistory _achievementHistory = null!;
    private readonly RecentAchievementsDisplay _recentDisplay = null!;
    private readonly GlobalHotkey _hotkey = null!;
    private readonly NotifyIcon _trayIcon = null!;
    private readonly ToolStripMenuItem _soundEnabledItem = null!;
    private readonly ToolStripMenuItem _pauseItem = null!;
    private readonly ToolStripMenuItem _startWithWindowsItem = null!;

    private Icon? _activeIcon;
    private Icon? _pausedIcon;
    private AddGameForm? _addGameForm;
    private bool _disposed;

    // Appids already evaluated for the synthetic "tracking configured" notification this session,
    // guarding against a double-fire from the startup scan and a live folder-creation event.
    private readonly HashSet<string> _trackingNotified = new();

    public TrayApplicationContext()
    {
        Logger.Init();

        var infoVersion = typeof(TrayApplicationContext).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0];
        var versionLabel = infoVersion != null && infoVersion != "1.0.0" ? $"v{infoVersion}" : "dev version";
        Logger.Info($"Achievement Overlay: {versionLabel}");

        try
        {
            _config = new AppConfig();
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            Logger.Error($"Config error: '{configPath}': {ex.Message}");
            var heading = ex is FileNotFoundException ? "Config file not found" : "Config file is invalid";
            var detail = ex switch
            {
                FileNotFoundException => "Expected config.json next to the executable.",
                JsonException je => je.Message.Split('.')[0] + ".",
                InvalidOperationException ioe => ioe.Message.Replace("Invalid config: ", ""),
                _ => "Check log file for more details."
            };
            ShowConfigError(heading, detail);
            return;
        }
        Logger.Info($"Config: gamesPaths='{string.Join(";", _config.GamesPaths)}', gseSavesPaths='{string.Join(";", _config.GseSavesPaths)}', language={_config.Language}, soundEnabled={_config.SoundEnabled}, soundPath='{_config.SoundPath}', displayDuration={_config.DisplayDuration}, recentAchievementsShortcut={_config.RecentAchievementsShortcut}, recentAchievementsCount={_config.RecentAchievementsCount}");

        _gameCache = new GameCache(_config);
        _gameCache.ScanAll();
        foreach (var game in _gameCache.GetAll())
            Logger.Info($"  {game.GameName}, appid={game.AppId}, path='{game.MetadataPath}'");

        _soundPlayer = new UnlockSoundPlayer(_config);
        _notificationQueue = new NotificationQueue(_gameCache, _config, _soundPlayer);

        var validSavesPaths = _config.GseSavesPaths.Where(p => { if (Directory.Exists(p)) return true; Logger.Warn($"GSE Saves path does not exist: '{p}'"); return false; }).ToArray();
        if (validSavesPaths.Length == 0)
        {
            ShowConfigError("Config file is invalid", "No valid 'gseSavesPaths' directories found.");
            return;
        }

        // An empty cache is no longer fatal: a game whose unlock file describes its own
        // achievements needs no steam_settings/ and no 'gamesPaths' entry, and exiting here would
        // hit exactly the user who has installed the overlay but not yet run the game — the one
        // case the folder watcher exists to catch.
        if (_gameCache.GetAll().Count == 0)
            Logger.Warn("No games with achievement metadata found under 'gamesPaths' — only games whose unlock file carries its own achievement names will be tracked.");

        _watcher = new AchievementWatcher(validSavesPaths);
        _watcher.NewAchievement += OnNewAchievement;
        _watcher.GameFolderObserved += OnGameFolderObserved;
        _watcher.Start();

        NotifyTrackingConfiguredForExistingFolders();

        _achievementHistory = new AchievementHistory(_config, _gameCache);
        _recentDisplay = new RecentAchievementsDisplay(_achievementHistory, _config, _soundPlayer);
        _hotkey = new GlobalHotkey(1, _config.RecentAchievementsShortcut, () => _recentDisplay.Toggle());
        if (!_hotkey.IsRegistered)
            Logger.Warn($"Could not register hotkey '{_config.RecentAchievementsShortcut}' — use the tray menu instead");

        _activeIcon = AppUtilities.LoadOrCreateIcon(false);
        _pausedIcon = AppUtilities.LoadOrCreateIcon(true);

        _soundEnabledItem = new ToolStripMenuItem("Sound enabled")
        {
            CheckOnClick = true,
            Checked = _config.SoundEnabled
        };
        _soundEnabledItem.CheckedChanged += (_, _) =>
        {
            _config.UpdateConfigValue(nameof(SettingsData.SoundEnabled), _soundEnabledItem.Checked);
            Logger.Info($"Sound enabled: {_soundEnabledItem.Checked}");
        };

        _pauseItem = new ToolStripMenuItem("Pause notifications")
        {
            CheckOnClick = true,
            Checked = false
        };
        _pauseItem.CheckedChanged += (_, _) =>
        {
            _notificationQueue.IsPaused = _pauseItem.Checked;
            _trayIcon!.Icon = _pauseItem.Checked ? _pausedIcon! : _activeIcon!;
            Logger.Info($"Notifications paused: {_pauseItem.Checked}");
        };

        _startWithWindowsItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = GetStartWithWindows()
        };
        _startWithWindowsItem.CheckedChanged += OnStartWithWindowsChanged;

        var openConfigItem = new ToolStripMenuItem("Open config/logs location");
        openConfigItem.Click += (_, _) =>
        {
            var settingsPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (File.Exists(settingsPath))
                Process.Start("explorer.exe", $"/select,\"{settingsPath}\"");
            else
                Process.Start("explorer.exe", AppContext.BaseDirectory);
        };

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();

        _trayIcon = new NotifyIcon
        {
            Icon = _activeIcon,
            Text = "Achievement Overlay",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };

        var recentItem = new ToolStripMenuItem("Show recent");
        if (_hotkey.IsRegistered)
            recentItem.ShortcutKeyDisplayString = _config.RecentAchievementsShortcut;
        recentItem.Click += (_, _) => _recentDisplay.Toggle();

        var addGameItem = new ToolStripMenuItem("Add game…");
        addGameItem.Click += (_, _) => OpenAddGameDialog();

        _trayIcon.ContextMenuStrip.Items.AddRange(new ToolStripItem[]
        {
            recentItem,
            addGameItem,
            new ToolStripSeparator(),
            _soundEnabledItem,
            _pauseItem,
            new ToolStripSeparator(),
            _startWithWindowsItem,
            openConfigItem,
            new ToolStripSeparator(),
            exitItem
        });

        Logger.Info("Achievement Overlay started.");
    }

    private void OnStartWithWindowsChanged(object? sender, EventArgs e)
    {
        try
        {
            AppConfig.SetStartWithWindows(_startWithWindowsItem.Checked);
            Logger.Info($"Start with Windows: {_startWithWindowsItem.Checked}");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to set Start with Windows: {ex.Message}");
            _startWithWindowsItem.CheckedChanged -= OnStartWithWindowsChanged;
            _startWithWindowsItem.Checked = !_startWithWindowsItem.Checked;
            _startWithWindowsItem.CheckedChanged += OnStartWithWindowsChanged;
        }
    }

    private void OnNewAchievement(object? sender, NewAchievementEventArgs e)
    {
        _notificationQueue.Enqueue(e);
    }

    private void OnGameFolderObserved(object? sender, GameFolderObservedEventArgs e)
    {
        TryNotifyTrackingConfigured(e.AppId, e.States);
    }

    /// <summary>
    /// Evaluates every already-existing GSE Saves folder for the synthetic "tracking configured"
    /// notification. Runs at startup (for games configured and run before this app started), and
    /// again after a game is added — a game configured mid-session may already have a folder from
    /// an earlier run, so its folder-creation event has been and gone.
    /// </summary>
    private void NotifyTrackingConfiguredForExistingFolders()
    {
        foreach (var appId in _watcher.GetExistingAppIdFolders())
            TryNotifyTrackingConfigured(appId, states: null);
    }

    /// <summary>
    /// Shows the synthetic "Achievement tracking configured" notification for a game the first time
    /// its GSE Saves folder is seen — once per game (persisted), and only while it has zero earned
    /// achievements (so it never competes with a real first unlock).
    /// A game qualifies either by being configured under 'gamesPaths' or by having an unlock file
    /// that describes its own achievements. Pass <paramref name="states"/> when they have already
    /// been read, so a file the emulator may still hold open is not read twice.
    /// </summary>
    private void TryNotifyTrackingConfigured(string appId, Dictionary<string, AchievementUnlockState>? states)
    {
        if (string.IsNullOrEmpty(appId))
            return;

        lock (_trackingNotified)
        {
            if (_trackingNotified.Contains(appId))
                return;

            var shown = _config.GetCurrent().TrackingConfigured ?? new Dictionary<string, long>();
            if (shown.ContainsKey(appId))
            {
                _trackingNotified.Add(appId);
                return;
            }

            states ??= ReadUnlockStates(appId, out _);
            var game = _gameCache.LookupCached(appId);
            var gameKnown = game != null || (states != null && AchievementMetadata.IsSelfDescribing(states));

            // Leave unqualified folders unguarded so they can be re-evaluated if the game becomes
            // configured, or starts describing itself, later this session.
            if (!gameKnown)
                return;

            if (!TrackingConfirmation.ShouldNotify(gameKnown: true, alreadyShown: false, CountEarnedAchievements(appId, states)))
                return;

            var gameName = game?.GameName ?? appId;
            _trackingNotified.Add(appId);
            _notificationQueue.EnqueueSynthetic(
                appId,
                TrackingConfirmation.Title,
                TrackingConfirmation.Description(gameName),
                EmbeddedAssets.GetTrackingConfiguredIconPath());
            var updated = new Dictionary<string, long>(shown) { [appId] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            _config.UpdateConfigValue(nameof(SettingsData.TrackingConfigured), updated);
            Logger.Info($"Showed 'tracking configured' for appid {appId} ({gameName}).");
        }
    }

    /// <summary>
    /// Counts earned achievements for a game. Returns 0 if no unlock file exists yet (the common
    /// case at folder-creation time), and null if a file exists but could not be read or parsed —
    /// a file locked mid-write by the emulator must not be reported as "no achievements earned yet".
    /// </summary>
    private int? CountEarnedAchievements(string appId, Dictionary<string, AchievementUnlockState>? states)
    {
        if (states == null)
        {
            states = ReadUnlockStates(appId, out var unreadable);
            if (states == null)
                return unreadable ? null : 0;
        }

        return states.Values.Count(s => s.Earned);
    }

    /// <summary>
    /// Reads a game's unlock states from the first GSE Saves path that has the file. Returns null
    /// when no file exists (<paramref name="unreadable"/> false) or none could be read (true).
    /// </summary>
    private Dictionary<string, AchievementUnlockState>? ReadUnlockStates(string appId, out bool unreadable)
    {
        unreadable = false;

        foreach (var gseSavesPath in _config.GseSavesPaths)
        {
            var file = Path.Combine(gseSavesPath, appId, "achievements.json");
            if (!File.Exists(file))
                continue;

            try
            {
                return AchievementMetadata.ParseUnlockStates(File.ReadAllText(file));
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not read achievements for appid {appId}: {ex.Message}");
                unreadable = true;
            }
        }

        return null;
    }

    private void OpenAddGameDialog()
    {
        if (_addGameForm != null)
        {
            _addGameForm.Activate();
            return;
        }

        _addGameForm = new AddGameForm(_config, RegisterNewGame);
        try
        {
            _addGameForm.ShowDialog();
        }
        finally
        {
            _addGameForm.Dispose();
            _addGameForm = null;
        }
    }

    /// <summary>
    /// Called after a game is configured: ensures its folder is covered by gamesPaths,
    /// then rescans so the overlay tracks it without a restart.
    /// </summary>
    private void RegisterNewGame(string gameDir)
    {
        var rootToAdd = GamesPathPlanner.PlanRootToAdd(_config.GamesPaths, gameDir);
        if (rootToAdd != null)
        {
            var raw = _config.GetCurrent().GamesPaths;
            var newRaw = string.IsNullOrWhiteSpace(raw) ? rootToAdd : raw.TrimEnd(';') + ";" + rootToAdd;
            _config.UpdateConfigValue(nameof(SettingsData.GamesPaths), newRaw);
            Logger.Info($"Added games path '{rootToAdd}' to config.");
        }

        _gameCache.ScanAll();
        _watcher.ReseedAll();
        Logger.Info($"Game cache now has {_gameCache.GetAll().Count} game(s) after Add game.");

        // This is the re-evaluation that TryNotifyTrackingConfigured leaves unguarded games for.
        NotifyTrackingConfiguredForExistingFolders();
    }

    private void ExitApplication()
    {
        Logger.Info("Shutting down...");
        Dispose();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
            return;
        _disposed = true;

        if (disposing)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _hotkey.Dispose();
            _recentDisplay.Dispose();
            _watcher.Dispose();
            _notificationQueue.Dispose();
            _soundPlayer.Dispose();
            _activeIcon?.Dispose();
            _pausedIcon?.Dispose();
            Logger.Close();
        }
        base.Dispose(disposing);
    }

    private static void ShowConfigError(string heading, string detail)
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "overlay.log");
        Logger.Close();
        var logContent = "";
        try { logContent = File.ReadAllText(logPath); } catch { }
        var page = new TaskDialogPage
        {
            Heading = heading,
            Text = detail,
            Icon = TaskDialogIcon.Error,
            Caption = "Achievement Overlay",
            Buttons = { TaskDialogButton.OK }
        };
        if (!string.IsNullOrEmpty(logContent))
            page.Expander = new TaskDialogExpander { Text = logContent, CollapsedButtonText = "Details", ExpandedButtonText = "Details", Position = TaskDialogExpanderPosition.AfterFootnote };
        TaskDialog.ShowDialog(page);
        Environment.Exit(1);
    }

    private static bool GetStartWithWindows()
    {
        try
        {
            return AppConfig.IsStartWithWindows();
        }
        catch
        {
            return false;
        }
    }
}
