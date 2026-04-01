using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace AchievementOverlay;

/// <summary>
/// Queued notification item with resolved display data.
/// </summary>
public sealed class NotificationItem
{
    public required string AchievementName { get; init; }
    public required string Description { get; init; }
    public string? IconPath { get; init; }
    public required string AppId { get; init; }
}

/// <summary>
/// Receives achievement unlock events, resolves metadata via GameCache,
/// and dispatches overlay notifications one at a time on the UI thread.
/// </summary>
public sealed class NotificationQueue : IDisposable
{
    private readonly GameCache _gameCache;
    private readonly AppConfig _config;
    private readonly UnlockSoundPlayer? _soundPlayer;
    private readonly Action<string>? _log;
    private readonly Dispatcher _dispatcher;

    private readonly ConcurrentQueue<NotificationItem> _queue = new();
    private int _isDispatching; // 0 = idle, 1 = dispatching; use Interlocked for thread safety
    private volatile bool _isPaused;
    private volatile bool _disposed;
    private DispatcherTimer? _pauseTimer;

    private static readonly TimeSpan GapBetweenNotifications = TimeSpan.FromMilliseconds(500);

    public bool IsPaused
    {
        get => _isPaused;
        set => _isPaused = value;
    }

    /// <summary>
    /// Number of items currently in the queue (for testing/diagnostics).
    /// </summary>
    public int Count => _queue.Count;

    public NotificationQueue(
        GameCache gameCache,
        AppConfig config,
        UnlockSoundPlayer? soundPlayer = null,
        Action<string>? log = null,
        Dispatcher? dispatcher = null)
    {
        _gameCache = gameCache;
        _config = config;
        _soundPlayer = soundPlayer;
        _log = log;
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    /// <summary>
    /// Enqueues a new achievement event. Resolves metadata and adds to the dispatch queue.
    /// Can be called from any thread.
    /// </summary>
    public void Enqueue(NewAchievementEventArgs args)
    {
        if (_disposed)
            return;

        var item = ResolveMetadata(args);
        if (item == null)
        {
            _log?.Invoke($"Skipping notification for {args.AppId}/{args.AchievementName} (no game metadata)");
            return;
        }

        _queue.Enqueue(item);
        _log?.Invoke($"Queued notification: {item.AchievementName} (queue size: {_queue.Count})");

        // Kick off dispatching if not already running (atomic check-and-set)
        if (Interlocked.CompareExchange(ref _isDispatching, 1, 0) == 0)
        {
            _dispatcher.BeginInvoke(DispatchNext);
        }
    }

    /// <summary>
    /// Resolves achievement metadata (display name, description, icon) from the game cache.
    /// </summary>
    internal NotificationItem? ResolveMetadata(NewAchievementEventArgs args)
    {
        var gameInfo = _gameCache.Lookup(args.AppId);
        if (gameInfo == null)
            return null;

        var definitions = GameCache.LoadDefinitions(gameInfo);
        if (definitions == null)
            return null;

        var definition = AchievementMetadata.FindDefinition(definitions, args.AchievementName);
        if (definition == null)
            return null;

        var language = _config.Language;
        var displayName = AchievementMetadata.GetDisplayText(definition.DisplayName, language);
        var description = AchievementMetadata.GetDisplayText(definition.Description, language);

        if (string.IsNullOrEmpty(displayName))
            displayName = args.AchievementName;

        var metadataDir = Path.GetDirectoryName(gameInfo.MetadataPath)!;
        var iconPath = AchievementMetadata.ResolveIconPath(definition, metadataDir);

        return new NotificationItem
        {
            AppId = args.AppId,
            AchievementName = displayName,
            Description = description,
            IconPath = iconPath
        };
    }

    private void DispatchNext()
    {
        if (_disposed)
        {
            Interlocked.Exchange(ref _isDispatching, 0);
            return;
        }

        if (_isPaused)
        {
            _log?.Invoke("Notifications paused, waiting to dispatch...");
            if (_pauseTimer == null)
            {
                _pauseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _pauseTimer.Tick += (_, _) =>
                {
                    _pauseTimer.Stop();
                    DispatchNext();
                };
            }
            _pauseTimer.Start();
            return;
        }

        if (!_queue.TryDequeue(out var item))
        {
            Interlocked.Exchange(ref _isDispatching, 0);
            // Re-check: an item may have been enqueued between TryDequeue and Exchange
            if (!_queue.IsEmpty && Interlocked.CompareExchange(ref _isDispatching, 1, 0) == 0)
            {
                _dispatcher.BeginInvoke(DispatchNext);
            }
            return;
        }

        try
        {
            var gameWindowRect = GetForegroundWindowRect();
            _log?.Invoke($"Showing notification: {item.AchievementName} at ({gameWindowRect.Left},{gameWindowRect.Top} {gameWindowRect.Width}x{gameWindowRect.Height})");

            _soundPlayer?.Play();

            var window = new NotificationWindow(_config.DisplayDuration);
            window.Closed += (_, _) =>
            {
                // After window closes, wait a short gap then show next
                var gapTimer = new DispatcherTimer { Interval = GapBetweenNotifications };
                gapTimer.Tick += (_, _) =>
                {
                    gapTimer.Stop();
                    DispatchNext();
                };
                gapTimer.Start();
            };

            window.Show(item.AchievementName, item.Description, item.IconPath, gameWindowRect);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Error dispatching notification: {ex.Message}");

            // Schedule next dispatch attempt after gap to avoid stalling the queue
            var retryTimer = new DispatcherTimer { Interval = GapBetweenNotifications };
            retryTimer.Tick += (_, _) =>
            {
                retryTimer.Stop();
                DispatchNext();
            };
            retryTimer.Start();
        }
    }

    /// <summary>
    /// Gets the work area of the monitor containing the foreground window.
    /// Uses WinForms Screen class which handles multi-monitor and DPI correctly.
    /// Falls back to the primary screen work area.
    /// </summary>
    internal static Rect GetForegroundWindowRect()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
                var wa = screen.WorkingArea;
                // Screen.WorkingArea returns physical pixels. WPF positions windows
                // using the primary monitor's DPI as the coordinate basis, so convert
                // all coordinates using the primary monitor's DPI scale.
                var primaryDpiScale = SystemParameters.PrimaryScreenHeight > 0
                    ? System.Windows.Forms.Screen.PrimaryScreen!.Bounds.Height / SystemParameters.PrimaryScreenHeight
                    : 1.0;
                return new Rect(wa.Left / primaryDpiScale, wa.Top / primaryDpiScale, wa.Width / primaryDpiScale, wa.Height / primaryDpiScale);
            }
        }
        catch
        {
            // Fall through to default
        }

        var area = SystemParameters.WorkArea;
        return new Rect(0, 0, area.Width, area.Height);
    }

    public void Dispose()
    {
        _disposed = true;
        _pauseTimer?.Stop();
        // Drain the queue
        while (_queue.TryDequeue(out _)) { }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

}
