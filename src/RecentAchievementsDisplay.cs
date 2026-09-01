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
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(entry.EarnedTime).LocalDateTime.ToString("MMM dd, HH:mm");
        var gameInfoLine = $"{entry.GameName} \u2014 {timestamp}";

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

    private void RegisterEscHotkey()
    {
        try
        {
            _escHotkey = new GlobalHotkey(ESC_HOTKEY_ID, "Escape", () => Dismiss());
            if (!_escHotkey.IsRegistered)
            {
                Logger.Info("Could not register Esc hotkey for dismiss");
                _escHotkey.Dispose();
                _escHotkey = null;
            }
        }
        catch
        {
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
        {
            try { window.DismissImmediately(); }
            catch { /* window may already be closed */ }
        }
        _windows.Clear();
    }

    public void Dispose()
    {
        Dismiss();
    }
}
