using System.Collections.Concurrent;
using System.IO;
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
    private readonly Action<string>? _warn;
    private readonly Dispatcher _dispatcher;

    private readonly ConcurrentQueue<NotificationItem> _queue = new();
    private int _isDispatching; // 0 = idle, 1 = dispatching; use Interlocked for thread safety
    private volatile bool _isPaused;
    private volatile bool _disposed;

    // Reusable timers — avoids allocating a new DispatcherTimer per notification
    private DispatcherTimer? _pauseTimer;
    private DispatcherTimer? _gapTimer;

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
        Action<string>? warn = null,
        Dispatcher? dispatcher = null)
    {
        _gameCache = gameCache;
        _config = config;
        _soundPlayer = soundPlayer;
        _log = log;
        _warn = warn;
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
        var resolved = AchievementMetadata.Resolve(_gameCache, args.AppId, args.AchievementName, _config.Language, _warn);
        if (resolved == null)
            return null;

        return new NotificationItem
        {
            AppId = args.AppId,
            AchievementName = resolved.DisplayName,
            Description = resolved.Description,
            IconPath = resolved.IconPath
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
            ScheduleRetry(_pauseTimer ??= CreateTimer(), TimeSpan.FromSeconds(1));
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
            var gameWindowRect = AppUtilities.GetForegroundWindowRect();
            _log?.Invoke($"Showing notification: {item.AchievementName} at ({gameWindowRect.Left},{gameWindowRect.Top} {gameWindowRect.Width}x{gameWindowRect.Height})");

            _soundPlayer?.Play();

            var window = new NotificationWindow(_config.DisplayDuration);
            window.Closed += (_, _) => ScheduleRetry(_gapTimer ??= CreateTimer(), GapBetweenNotifications);

            window.Show(item.AchievementName, item.Description, item.IconPath, gameWindowRect);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Error dispatching notification: {ex.Message}");
            ScheduleRetry(_gapTimer ??= CreateTimer(), GapBetweenNotifications);
        }
    }

    private DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer();
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            DispatchNext();
        };
        return timer;
    }

    private static void ScheduleRetry(DispatcherTimer timer, TimeSpan interval)
    {
        timer.Stop();
        timer.Interval = interval;
        timer.Start();
    }

    public void Dispose()
    {
        _disposed = true;
        _pauseTimer?.Stop();
        _gapTimer?.Stop();
        while (_queue.TryDequeue(out _)) { }
    }
}
