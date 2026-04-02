using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AchievementOverlay;

/// <summary>
/// WinForms application context that manages the system tray icon, context menu,
/// and wires together all components (AchievementWatcher, GameCache, NotificationQueue).
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly AppConfig _config;
    private readonly GameCache _gameCache;
    private readonly AchievementWatcher _watcher;
    private readonly NotificationQueue _notificationQueue;
    private readonly UnlockSoundPlayer _soundPlayer;
    private readonly AchievementHistory _achievementHistory;
    private readonly RecentAchievementsDisplay _recentDisplay;
    private readonly GlobalHotkey _hotkey;
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _soundEnabledItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _startWithWindowsItem;
    private readonly StreamWriter? _logWriter;

    private Icon? _activeIcon;
    private Icon? _pausedIcon;
    private bool _disposed;

    public TrayApplicationContext()
    {
        _logWriter = AppUtilities.InitLog();
        Action<string> log = msg => Log("INFO", msg);
        Action<string> warn = msg => Log("WARN", msg);

        _config = new AppConfig();
        Log("INFO", $"Config loaded. GSE Saves: '{_config.GseSavesPath}', Games paths: '{string.Join(";", _config.GamesPaths)}'");

        _gameCache = new GameCache(_config, log);
        _gameCache.ScanAll();
        foreach (var game in _gameCache.GetAll())
            Log("INFO", $"  {game.GameName}, appid={game.AppId}, path='{game.MetadataPath}'");

        _soundPlayer = new UnlockSoundPlayer(_config, log);
        _notificationQueue = new NotificationQueue(_gameCache, _config, _soundPlayer, log);

        _watcher = new AchievementWatcher(_config.GseSavesPath, log, warn);
        _watcher.NewAchievement += OnNewAchievement;
        _watcher.Start(_gameCache.GetAllAppIds());

        _achievementHistory = new AchievementHistory(_config, _gameCache, log);
        _recentDisplay = new RecentAchievementsDisplay(_achievementHistory, _config, log);
        _hotkey = new GlobalHotkey(1, _config.RecentAchievementsShortcut, () => _recentDisplay.Toggle());
        if (!_hotkey.IsRegistered)
            Log("WARN", $"Could not register hotkey '{_config.RecentAchievementsShortcut}' — use the tray menu instead");

        _activeIcon = AppUtilities.LoadOrCreateIcon(false);
        _pausedIcon = AppUtilities.LoadOrCreateIcon(true);

        _soundEnabledItem = new ToolStripMenuItem("Sound Enabled")
        {
            CheckOnClick = true,
            Checked = _config.SoundEnabled
        };
        _soundEnabledItem.CheckedChanged += (_, _) =>
        {
            _config.UpdateConfigValue(nameof(SettingsData.SoundEnabled), _soundEnabledItem.Checked);
            Log("INFO", $"Sound enabled: {_soundEnabledItem.Checked}");
        };

        _pauseItem = new ToolStripMenuItem("Pause Notifications")
        {
            CheckOnClick = true,
            Checked = false
        };
        _pauseItem.CheckedChanged += (_, _) =>
        {
            _notificationQueue.IsPaused = _pauseItem.Checked;
            _trayIcon!.Icon = _pauseItem.Checked ? _pausedIcon! : _activeIcon!;
            Log("INFO", $"Notifications paused: {_pauseItem.Checked}");
        };

        _startWithWindowsItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = GetStartWithWindows()
        };
        _startWithWindowsItem.CheckedChanged += OnStartWithWindowsChanged;

        var openConfigItem = new ToolStripMenuItem("Open Config Location");
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

        var shortcutText = _config.RecentAchievementsShortcut;
        var recentItem = new ToolStripMenuItem("Show Recent") { ShortcutKeyDisplayString = shortcutText };
        recentItem.Click += (_, _) => _recentDisplay.Toggle();

        _trayIcon.ContextMenuStrip.Items.AddRange(new ToolStripItem[]
        {
            recentItem,
            new ToolStripSeparator(),
            _soundEnabledItem,
            _pauseItem,
            new ToolStripSeparator(),
            _startWithWindowsItem,
            openConfigItem,
            new ToolStripSeparator(),
            exitItem
        });

        Log("INFO", "Achievement Overlay started.");
    }

    private void OnStartWithWindowsChanged(object? sender, EventArgs e)
    {
        try
        {
            AppConfig.SetStartWithWindows(_startWithWindowsItem.Checked);
            Log("INFO", $"Start with Windows: {_startWithWindowsItem.Checked}");
        }
        catch (Exception ex)
        {
            Log("ERROR", $"Failed to set Start with Windows: {ex.Message}");
            _startWithWindowsItem.CheckedChanged -= OnStartWithWindowsChanged;
            _startWithWindowsItem.Checked = !_startWithWindowsItem.Checked;
            _startWithWindowsItem.CheckedChanged += OnStartWithWindowsChanged;
        }
    }

    private void OnNewAchievement(object? sender, NewAchievementEventArgs e)
    {
        _notificationQueue.Enqueue(e);
    }

    private void ExitApplication()
    {
        Log("INFO", "Shutting down...");
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
            _logWriter?.Dispose();
        }
        base.Dispose(disposing);
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

    private void Log(string level, string message)
    {
        AppUtilities.Log(_logWriter, level, message);
    }
}
