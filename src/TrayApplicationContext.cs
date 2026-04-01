using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
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
        // Logging
        _logWriter = InitLog();
        Action<string> log = Log;

        // Config
        _config = new AppConfig();
        Log($"Config loaded. GSE Saves: '{_config.GseSavesPath}', Games paths: '{string.Join(";", _config.GamesPaths)}'");

        // Game cache — accepts AppConfig so it reads gamesPaths dynamically on each scan,
        // recovering if config.json was unreadable during initial load
        _gameCache = new GameCache(_config, log);
        _gameCache.ScanAll();
        foreach (var game in _gameCache.GetAll())
            Log($"  {game.GameName}, appid={game.AppId}, path='{game.MetadataPath}'");

        // Sound player
        _soundPlayer = new UnlockSoundPlayer(_config, log);

        // Notification queue (needs WPF dispatcher — created on the STA thread)
        _notificationQueue = new NotificationQueue(_gameCache, _config, _soundPlayer, log);

        _watcher = new AchievementWatcher(_config.GseSavesPath, log);
        _watcher.NewAchievement += OnNewAchievement;
        _watcher.Start(_gameCache.GetAllAppIds());

        // Tray icon and menu
        _activeIcon = LoadOrCreateIcon(false);
        _pausedIcon = LoadOrCreateIcon(true);

        _soundEnabledItem = new ToolStripMenuItem("Sound Enabled")
        {
            CheckOnClick = true,
            Checked = _config.SoundEnabled
        };
        _soundEnabledItem.CheckedChanged += (_, _) =>
        {
            _config.UpdateConfigValue(nameof(SettingsData.SoundEnabled), _soundEnabledItem.Checked);
            Log($"Sound enabled: {_soundEnabledItem.Checked}");
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
            Log($"Notifications paused: {_pauseItem.Checked}");
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

        _trayIcon.ContextMenuStrip.Items.AddRange(new ToolStripItem[]
        {
            _soundEnabledItem,
            _pauseItem,
            new ToolStripSeparator(),
            _startWithWindowsItem,
            openConfigItem,
            new ToolStripSeparator(),
            exitItem
        });

        Log("Achievement Overlay started.");
    }

    private void OnStartWithWindowsChanged(object? sender, EventArgs e)
    {
        try
        {
            AppConfig.SetStartWithWindows(_startWithWindowsItem.Checked);
            Log($"Start with Windows: {_startWithWindowsItem.Checked}");
        }
        catch (Exception ex)
        {
            Log($"Failed to set Start with Windows: {ex.Message}");
            // Unsubscribe before reverting to prevent re-entrant call
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
        Log("Shutting down...");
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

    private static Icon LoadOrCreateIcon(bool grayscale)
    {
        var stream = typeof(TrayApplicationContext).Assembly.GetManifestResourceStream("AchievementOverlay.icon.ico");
        if (stream != null)
        {
            var icon = new Icon(stream);
            if (!grayscale) return icon;

            using (icon)
            using (var bmp = icon.ToBitmap())
            using (var grayBmp = ToGrayscale(bmp))
            {
                return CloneIconFromHandle(grayBmp.GetHicon());
            }
        }

        // Create a simple programmatic icon
        return CreateDefaultIcon(grayscale);
    }

    private static Icon CreateDefaultIcon(bool grayscale)
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var fillColor = grayscale ? Color.Gray : Color.FromArgb(0xDA, 0xA5, 0x20); // Goldenrod
        var borderColor = grayscale ? Color.DarkGray : Color.FromArgb(0xFF, 0xD7, 0x00); // Gold

        using var fillBrush = new SolidBrush(fillColor);
        using var borderPen = new Pen(borderColor, 1);
        g.FillEllipse(fillBrush, 1, 1, 13, 13);
        g.DrawEllipse(borderPen, 1, 1, 13, 13);

        // Star in center
        var starColor = grayscale ? Color.LightGray : Color.FromArgb(0xFF, 0xF8, 0xDC);
        using var font = new Font("Segoe UI", 7f, FontStyle.Bold);
        using var starBrush = new SolidBrush(starColor);
        var starSize = g.MeasureString("\u2605", font);
        g.DrawString("\u2605", font, starBrush,
            (16 - starSize.Width) / 2,
            (16 - starSize.Height) / 2);

        return CloneIconFromHandle(bmp.GetHicon());
    }

    /// <summary>
    /// Creates a managed Icon clone from an HICON handle and frees the native handle.
    /// Icon.FromHandle does not take ownership, so the HICON must be destroyed separately.
    /// </summary>
    private static Icon CloneIconFromHandle(IntPtr hIcon)
    {
        using var tempIcon = Icon.FromHandle(hIcon);
        var clone = (Icon)tempIcon.Clone();
        DestroyIcon(hIcon);
        return clone;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static Bitmap ToGrayscale(Bitmap source)
    {
        var gray = new Bitmap(source.Width, source.Height);
        for (var x = 0; x < source.Width; x++)
        {
            for (var y = 0; y < source.Height; y++)
            {
                var pixel = source.GetPixel(x, y);
                var lum = (int)(pixel.R * 0.299 + pixel.G * 0.587 + pixel.B * 0.114);
                gray.SetPixel(x, y, Color.FromArgb(pixel.A, lum, lum, lum));
            }
        }
        return gray;
    }

    // --- Logging ---

    private static StreamWriter? InitLog()
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "achievement-overlay.log");
            return new StreamWriter(logPath, append: false) { AutoFlush = true };
        }
        catch
        {
            return null;
        }
    }

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        _logWriter?.WriteLine(line);
    }
}
