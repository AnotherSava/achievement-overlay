using System.Collections.Concurrent;
using System.Windows.Threading;
using AchievementOverlay.GbeOverlay;

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
    private readonly GbeOverlaySettingsReader? _overlayReader;
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
        Dispatcher? dispatcher = null,
        GbeOverlaySettingsReader? overlayReader = null)
    {
        _gameCache = gameCache;
        _config = config;
        _soundPlayer = soundPlayer;
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
        _overlayReader = overlayReader;
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
            Logger.Warn($"Skipping notification for {args.AppId}/{args.AchievementName} — no metadata (game not found under 'gamesPaths', and the unlock file carries no displayName)");
            return;
        }

        EnqueueItem(item);
    }

    /// <summary>
    /// Enqueues a synthetic notification with pre-resolved display data, bypassing the
    /// game-schema lookup. Used for app-generated notifications (e.g. "tracking configured")
    /// whose achievement name is not present in any game's achievements.json.
    /// </summary>
    public void EnqueueSynthetic(string appId, string title, string description, string? iconPath)
    {
        if (_disposed)
            return;

        EnqueueItem(new NotificationItem
        {
            AppId = appId,
            AchievementName = title,
            Description = description,
            IconPath = iconPath
        });
    }

    private void EnqueueItem(NotificationItem item)
    {
        _queue.Enqueue(item);
        Logger.Info($"Queued notification: {item.AchievementName} (queue size: {_queue.Count})");

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
        var resolved = AchievementMetadata.Resolve(
            _gameCache, args.AppId, args.AchievementName, args.UnlockState, _config.Language);
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
            Logger.Info("Notifications paused, waiting to dispatch...");
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
            Logger.Info($"Showing notification: {item.AchievementName} at ({gameWindowRect.Left},{gameWindowRect.Top} {gameWindowRect.Width}x{gameWindowRect.Height})");

            var appearance = ResolveAppearance(item.AppId);
            _soundPlayer?.Play(appearance.SoundEnabled, appearance.SoundPath, appearance.SoundIsFromGame);

            var window = new NotificationWindow(appearance);
            window.Closed += (_, _) => ScheduleRetry(_gapTimer ??= CreateTimer(), GapBetweenNotifications);

            window.ShowNotification(item.AchievementName, item.Description, item.IconPath, gameWindowRect);
        }
        catch (Exception ex)
        {
            Logger.Info($"Error dispatching notification: {ex.Message}");
            ScheduleRetry(_gapTimer ??= CreateTimer(), GapBetweenNotifications);
        }
    }

    /// <summary>
    /// How this game's popup is drawn: the app's settings, overridden by whatever the game's own
    /// <c>steam_settings/</c> says. <see cref="GameCache.LookupCached"/> rather than a rescanning
    /// lookup — this runs per unlock, and a game that isn't in the cache (one tracked purely through
    /// a self-describing unlock file, or one outside 'gamesPaths') has no folder to read either way.
    /// </summary>
    private NotificationAppearance ResolveAppearance(string appId)
    {
        var settings = _config.GetCurrent();
        if (!settings.UseGameOverlaySettings || _overlayReader == null)
            return NotificationAppearance.From(settings);

        var game = _gameCache.LookupCached(appId);
        return NotificationAppearance.Resolve(settings, _overlayReader.Read(game?.SettingsDirs));
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
