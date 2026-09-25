using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;

namespace AchievementOverlay;

/// <summary>
/// Orchestrates displaying N recent achievements as stacked notification windows with sequential
/// cascade animation. A footer notification with dismiss instructions appears first, flush against the
/// configured edge, then achievements cascade away from it.
/// </summary>
public sealed class RecentAchievementsDisplay : IDisposable
{
    private readonly AchievementHistory _history;
    private readonly AppConfig _config;
    private readonly UnlockSoundPlayer? _soundPlayer;
    private readonly List<NotificationWindow> _windows = new();
    private GlobalHotkey? _escHotkey;
    private DispatcherTimer? _cascadeTimer;
    private const int ESC_HOTKEY_ID = 9999;
    private DateTime _lastShowTime;

    /// <summary>
    /// Appids whose earned time has been logged as not being a date, so each game is reported once per
    /// session rather than on every press of the shortcut.
    /// </summary>
    private readonly HashSet<string> _undatedAppIds = new();

    /// <summary>The Unix seconds <see cref="DateTimeOffset"/> can hold: 0001-01-01 to 9999-12-31 23:59:59 UTC.</summary>
    private static readonly long MinUnixSeconds = DateTimeOffset.MinValue.ToUnixTimeSeconds();
    private static readonly long MaxUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

    public bool IsVisible => _windows.Count > 0;

    public RecentAchievementsDisplay(AchievementHistory history, AppConfig config, UnlockSoundPlayer? soundPlayer = null)
    {
        _history = history;
        _config = config;
        _soundPlayer = soundPlayer;
    }

    public void Toggle()
    {
        Logger.Info($"Toggle called, IsVisible={IsVisible}, window count={_windows.Count}");
        if (IsVisible)
        {
            if ((DateTime.UtcNow - _lastShowTime).TotalMilliseconds < 1000)
                return;
            Dismiss();
        }
        else
        {
            Show();
        }
    }

    public void Show()
    {
        if (IsVisible)
            Dismiss();

        var entries = _history.GetRecent(_config.RecentAchievementsCount);
        if (entries.Count == 0)
            return;

        _lastShowTime = DateTime.UtcNow;
        Logger.Info($"Showing {entries.Count} recent achievement(s)");

        var gameWindowRect = AppUtilities.GetForegroundWindowRect();
        // The settings dialog allows clearing the shortcut, leaving the tray menu as the way in.
        var shortcut = _config.RecentAchievementsShortcut;
        var dismissHint = string.IsNullOrWhiteSpace(shortcut) ? "Press Esc to hide" : $"Press {shortcut} or Esc to hide";

        // Resolved once and shared by every window in the panel: a settings save mid-cascade must not
        // be able to leave half the stack in one corner and half in another.
        var appearance = NotificationAppearance.From(_config);
        var anchor = appearance.Anchor;

        // The footer alone sits flush against the anchored edge; an unlock popup rests one slide
        // distance further in. The two have always differed by that much, and still do.
        var flushEdge = NotificationPlacement.FlushEdge(anchor, gameWindowRect);

        // Show footer first (info bar with dismiss instructions)
        var footer = new NotificationWindow(appearance);
        var footerTop = NotificationPlacement.TopFor(anchor, flushEdge, 40); // rough estimate, corrected after render
        footer.ShowFooter($"Achievement Overlay \u2014 Recent achievements\n\n{dismissHint}", gameWindowRect, footerTop,
            NotificationPlacement.SlideOffset(anchor, gameWindowRect));
        _windows.Add(footer);

        // After footer renders, position correctly and start cascading achievements
        var ctx = new CascadeContext
        {
            Entries = entries,
            GameWindowRect = gameWindowRect,
            Appearance = appearance,
        };

        footer.Dispatcher.BeginInvoke(() =>
        {
            var footerHeight = footer.ActualHeight > 0 ? footer.ActualHeight : 40;
            footer.Top = NotificationPlacement.TopFor(anchor, flushEdge, footerHeight);
            ctx.NextEdge = NotificationPlacement.Advance(anchor, flushEdge, footerHeight);

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                ShowNext(0, ctx);
            };
            timer.Start();
        }, DispatcherPriority.Loaded);
    }

    private sealed class CascadeContext
    {
        public List<AchievementHistoryEntry> Entries { get; init; } = null!;
        public Rect GameWindowRect { get; init; }

        /// <summary>Shared by every window in the panel, so the whole stack agrees on one anchor.</summary>
        public NotificationAppearance Appearance { get; init; } = null!;

        /// <summary>Near edge of the next free slot, walked away from the anchor as entries are added.</summary>
        public double NextEdge { get; set; }
    }

    private void ShowNext(int index, CascadeContext ctx)
    {
        if (index >= ctx.Entries.Count)
        {
            RegisterEscHotkey();
            return;
        }

        var entry = ctx.Entries[index];
        var earnedAt = LocalEarnedTime(entry.EarnedTime, TimeZoneInfo.Local, CultureInfo.CurrentCulture);
        if (earnedAt == null && _undatedAppIds.Add(entry.AppId))
            Logger.Warn($"Recent achievements: appid {entry.AppId} has earned_time {entry.EarnedTime} on '{entry.AchievementName}', outside the range a date can hold; its entries like this are listed without a time (logged once per game)");
        // An unknown time is left off the line rather than replaced with one.
        var gameInfoLine = earnedAt is { } local ? $"{entry.GameName} \u2014 {local.ToString("MMM dd, HH:mm", CultureInfo.CurrentCulture)}" : entry.GameName;

        var window = new NotificationWindow(ctx.Appearance);
        var anchor = ctx.Appearance.Anchor;

        var estimatedHeight = 80.0;
        var finalTop = NotificationPlacement.TopFor(anchor, ctx.NextEdge, estimatedHeight);
        var slideOffset = NotificationPlacement.StackSlideOffset(anchor, estimatedHeight);

        // App settings, never a game's: the panel stacks entries from several games at once, so no
        // one game's config can speak for the stack.
        _soundPlayer?.Play(_config.SoundEnabled, _config.SoundPath);
        window.ShowRecent(entry.AchievementName, entry.Description, entry.IconPath, ctx.GameWindowRect, finalTop, slideOffset, gameInfoLine);
        _windows.Add(window);

        window.Dispatcher.BeginInvoke(() =>
        {
            var actualHeight = window.ActualHeight > 0 ? window.ActualHeight : estimatedHeight;
            window.Top = NotificationPlacement.TopFor(anchor, ctx.NextEdge, actualHeight);
            ctx.NextEdge = NotificationPlacement.Advance(anchor, ctx.NextEdge, actualHeight);

            if (index + 1 < ctx.Entries.Count)
            {
                _cascadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                _cascadeTimer.Tick += (_, _) =>
                {
                    _cascadeTimer.Stop();
                    ShowNext(index + 1, ctx);
                };
                _cascadeTimer.Start();
            }
            else
            {
                RegisterEscHotkey();
            }
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// The clock time in <paramref name="zone"/> at which an achievement was earned, or null when
    /// <paramref name="culture"/>'s calendar cannot write it — epoch milliseconds are enough to pass
    /// year 9999, and some calendars end far sooner (Um al-Qura, ar-SA's default, in 2077). Null means
    /// the time is unknown, including an instant inside the UTC range whose clock time in the zone falls
    /// past either end, which the framework's own conversions would clamp to the extreme date instead.
    /// </summary>
    internal static DateTime? LocalEarnedTime(long earnedTime, TimeZoneInfo zone, CultureInfo culture)
    {
        if (earnedTime < MinUnixSeconds || earnedTime > MaxUnixSeconds)
            return null;

        var utc = DateTimeOffset.FromUnixTimeSeconds(earnedTime).UtcDateTime;
        var localTicks = utc.Ticks + zone.GetUtcOffset(utc).Ticks;
        var calendar = culture.DateTimeFormat.Calendar;
        if (localTicks < calendar.MinSupportedDateTime.Ticks || localTicks > calendar.MaxSupportedDateTime.Ticks)
            return null;

        return new DateTime(localTicks, DateTimeKind.Unspecified);
    }

    private void RegisterEscHotkey()
    {
        try
        {
            _escHotkey = new GlobalHotkey(ESC_HOTKEY_ID, "Escape", Dismiss);
            if (!_escHotkey.IsRegistered)
            {
                Logger.Info("Could not register Esc hotkey for dismiss");
                _escHotkey.Dispose();
                _escHotkey = null;
            }
        }
        catch (Win32Exception ex)
        {
            Logger.Warn($"Could not create the window for the Esc hotkey: {ex.Message}");
            _escHotkey = null;
        }
    }

    public void Dismiss()
    {
        Logger.Info("Dismissing recent achievements display");

        _cascadeTimer?.Stop();
        _cascadeTimer = null;
        _escHotkey?.Dispose();
        _escHotkey = null;

        foreach (var window in _windows)
            window.DismissImmediately();
        _windows.Clear();
    }

    public void Dispose() => Dismiss();
}
