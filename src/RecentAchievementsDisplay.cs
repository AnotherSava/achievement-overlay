using System.Windows;
using System.Windows.Threading;

namespace AchievementOverlay;

/// <summary>
/// Orchestrates displaying N recent achievements as stacked notification windows
/// with sequential cascade animation. A footer notification with dismiss instructions
/// appears first at the bottom, then achievements cascade upward above it.
/// </summary>
public sealed class RecentAchievementsDisplay : IDisposable
{
    private readonly AchievementHistory _history;
    private readonly AppConfig _config;
    private readonly Action<string>? _log;
    private readonly List<NotificationWindow> _windows = new();
    private GlobalHotkey? _escHotkey;
    private const int ESC_HOTKEY_ID = 9999;
    private const double GapBetweenWindows = 6;
    private DateTime _lastShowTime;

    public bool IsVisible => _windows.Count > 0;

    public RecentAchievementsDisplay(AchievementHistory history, AppConfig config, Action<string>? log = null)
    {
        _history = history;
        _config = config;
        _log = log;
    }

    public void Toggle()
    {
        _log?.Invoke($"Toggle called, IsVisible={IsVisible}, window count={_windows.Count}");
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
        _log?.Invoke($"Showing {entries.Count} recent achievement(s)");

        var gameWindowRect = AppUtilities.GetForegroundWindowRect();
        var shortcut = _config.RecentAchievementsShortcut;
        var notificationWidth = Math.Max(250, gameWindowRect.Width * 0.15);
        var margin = Math.Min(gameWindowRect.Width, gameWindowRect.Height) * 0.02;
        var standardSlideDistance = gameWindowRect.Height * 0.015;

        // Show footer first (info bar with dismiss instructions)
        var footer = new NotificationWindow(_config.DisplayDuration);
        var footerTop = gameWindowRect.Bottom - margin - 40; // rough estimate, corrected after render
        footer.ShowFooter($"Achievement Overlay \u2014 Recent achievements\n\nPress {shortcut} or Esc to hide", gameWindowRect, footerTop, standardSlideDistance);
        _windows.Add(footer);

        // After footer renders, position correctly and start cascading achievements
        var ctx = new CascadeContext
        {
            Entries = entries,
            GameWindowRect = gameWindowRect,
            NotificationWidth = notificationWidth,
            Margin = margin,
            StandardSlideDistance = standardSlideDistance,
        };

        footer.Dispatcher.BeginInvoke(() =>
        {
            var footerHeight = footer.ActualHeight > 0 ? footer.ActualHeight : 40;
            footer.Top = gameWindowRect.Bottom - footerHeight - margin;
            ctx.NextBottomEdge = footer.Top - GapBetweenWindows;

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
        public double NotificationWidth { get; init; }
        public double Margin { get; init; }
        public double StandardSlideDistance { get; init; }
        public double NextBottomEdge { get; set; }
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

        var window = new NotificationWindow(_config.DisplayDuration);

        var estimatedHeight = 80.0;
        var finalTop = ctx.NextBottomEdge - estimatedHeight;
        double slideUpDistance = estimatedHeight + GapBetweenWindows;

        window.ShowRecent(entry.AchievementName, entry.Description, entry.IconPath, ctx.GameWindowRect, finalTop, slideUpDistance, gameInfoLine);
        _windows.Add(window);

        window.Dispatcher.BeginInvoke(() =>
        {
            var actualHeight = window.ActualHeight > 0 ? window.ActualHeight : estimatedHeight;
            window.Top = ctx.NextBottomEdge - actualHeight;
            ctx.NextBottomEdge = window.Top - GapBetweenWindows;

            if (index + 1 < ctx.Entries.Count)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    ShowNext(index + 1, ctx);
                };
                timer.Start();
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
                _log?.Invoke("Could not register Esc hotkey for dismiss");
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
        _log?.Invoke("Dismissing recent achievements display");

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
