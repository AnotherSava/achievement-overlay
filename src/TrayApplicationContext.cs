using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text.Json;
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

    private Icon? _activeIcon;
    private Icon? _pausedIcon;
    private bool _disposed;

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
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            Logger.Error($"Config file is invalid or unreadable: '{configPath}': {ex.Message}");
            MessageBox.Show($"Config file error:\n{configPath}\n\n{ex.Message}\n\nFix the file and restart.", "Achievement Overlay", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.Exit(1);
            return;
        }
        Logger.Info($"Config: gseSavesPath='{_config.GseSavesPath}', gamesPaths='{string.Join(";", _config.GamesPaths)}', language={_config.Language}, soundEnabled={_config.SoundEnabled}, soundPath='{_config.SoundPath}', displayDuration={_config.DisplayDuration}, recentAchievementsShortcut={_config.RecentAchievementsShortcut}, recentAchievementsCount={_config.RecentAchievementsCount}");

        _gameCache = new GameCache(_config);
        _gameCache.ScanAll();
        foreach (var game in _gameCache.GetAll())
            Logger.Info($"  {game.GameName}, appid={game.AppId}, path='{game.MetadataPath}'");

        _soundPlayer = new UnlockSoundPlayer(_config);
        _notificationQueue = new NotificationQueue(_gameCache, _config, _soundPlayer);

        _watcher = new AchievementWatcher(_config.GseSavesPath);
        _watcher.NewAchievement += OnNewAchievement;
        _watcher.Start(_gameCache.GetAllAppIds());

        _achievementHistory = new AchievementHistory(_config, _gameCache);
        _recentDisplay = new RecentAchievementsDisplay(_achievementHistory, _config, _soundPlayer);
        _hotkey = new GlobalHotkey(1, _config.RecentAchievementsShortcut, () => _recentDisplay.Toggle());
        if (!_hotkey.IsRegistered)
            Logger.Warn($"Could not register hotkey '{_config.RecentAchievementsShortcut}' — use the tray menu instead");

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
            Logger.Info($"Sound enabled: {_soundEnabledItem.Checked}");
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
            Logger.Info($"Notifications paused: {_pauseItem.Checked}");
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

        var recentItem = new ToolStripMenuItem("Show Recent");
        if (_hotkey.IsRegistered)
            recentItem.ShortcutKeyDisplayString = _config.RecentAchievementsShortcut;
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
