using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AchievementOverlay;

public partial class NotificationWindow : Window
{
    private const double WindowWidthFraction = 0.15;
    private const double IconSizeFraction = 0.25;
    private const double MarginFraction = 0.02;
    private const double SlideDistanceFraction = 0.015;

    private static readonly Duration SlideDuration = new(TimeSpan.FromMilliseconds(300));
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(500));
    private readonly DispatcherTimer _holdTimer;
    private double _slideDistance;
    private bool _recentMode;

    public NotificationWindow(int displayDurationSeconds = 10)
    {
        InitializeComponent();
        Opacity = 0;
        _holdTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(displayDurationSeconds) };
        _holdTimer.Tick += (_, _) =>
        {
            _holdTimer.Stop();
            StartFadeOut();
        };
    }

    /// <summary>
    /// Shows the notification window positioned at the bottom-right of the given game window rectangle.
    /// </summary>
    /// <param name="achievementName">Display name of the achievement.</param>
    /// <param name="description">Achievement description text.</param>
    /// <param name="iconPath">Path to the achievement icon file, or null for default.</param>
    /// <param name="gameWindowRect">Rectangle of the game window (left, top, width, height).</param>
    public void ShowNotification(string achievementName, string description, string? iconPath, Rect gameWindowRect)
    {
        AchievementName.Text = achievementName;
        AchievementDescription.Text = description;
        AchievementDescription.Visibility = string.IsNullOrEmpty(description) ? Visibility.Collapsed : Visibility.Visible;

        LoadIcon(iconPath, gameWindowRect.Width);
        SizeAndPosition(gameWindowRect);
        Show();
        StartSlideIn();
    }

    /// <summary>
    /// Shows as a footer info bar — no icon, no title, just text. No auto-dismiss.
    /// </summary>
    public void ShowFooter(string text, Rect gameWindowRect, double customTop, double slideUpDistance)
    {
        AchievementIcon.Visibility = Visibility.Collapsed;
        AchievementName.Text = text;
        AchievementName.FontWeight = FontWeights.Normal;
        AchievementName.FontSize = 11;
        AchievementName.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xBB, 0xFF, 0xFF, 0xFF));
        AchievementName.TextAlignment = TextAlignment.Center;
        AchievementDescription.Visibility = Visibility.Collapsed;

        var notificationWidth = Math.Max(250, gameWindowRect.Width * WindowWidthFraction);
        MaxWidth = notificationWidth;
        MinWidth = notificationWidth;
        var margin = Math.Min(gameWindowRect.Width, gameWindowRect.Height) * MarginFraction;
        Left = gameWindowRect.Right - notificationWidth - margin;
        Top = customTop;
        _slideDistance = slideUpDistance;
        _recentMode = true;

        Show();
        StartSlideIn();
    }

    /// <summary>
    /// Shows as a "recent" notification — custom slide distance, extra text lines, no auto-dismiss.
    /// </summary>
    public void ShowRecent(string achievementName, string description, string? iconPath, Rect gameWindowRect, double customTop, double slideUpDistance, string? gameInfoLine)
    {
        AchievementName.Text = achievementName;
        AchievementDescription.Text = description;
        AchievementDescription.Visibility = string.IsNullOrEmpty(description) ? Visibility.Collapsed : Visibility.Visible;

        if (!string.IsNullOrEmpty(gameInfoLine))
        {
            GameInfoText.Text = gameInfoLine;
            GameInfoText.Visibility = Visibility.Visible;
        }

        LoadIcon(iconPath, gameWindowRect.Width);

        var notificationWidth = Math.Max(250, gameWindowRect.Width * WindowWidthFraction);
        MaxWidth = notificationWidth;
        MinWidth = notificationWidth;
        var margin = Math.Min(gameWindowRect.Width, gameWindowRect.Height) * MarginFraction;
        Left = gameWindowRect.Right - notificationWidth - margin;
        Top = customTop;
        _slideDistance = slideUpDistance;
        _recentMode = true;

        Show();
        StartSlideIn();
    }

    /// <summary>
    /// Triggers fade-out animation then closes the window.
    /// </summary>
    public void DismissImmediately()
    {
        _holdTimer.Stop();
        StartFadeOut();
    }

    private void LoadIcon(string? iconPath, double gameWindowWidth)
    {
        var iconSize = Math.Max(32, gameWindowWidth * WindowWidthFraction * IconSizeFraction);
        AchievementIcon.Width = iconSize;
        AchievementIcon.Height = iconSize;

        if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = (int)iconSize;
                bitmap.EndInit();
                bitmap.Freeze();
                AchievementIcon.Source = bitmap;
                return;
            }
            catch
            {
                // Fall through to default
            }
        }

        // Default trophy-like icon: a simple gold circle
        AchievementIcon.Source = CreateDefaultIcon(iconSize);
    }

    private static BitmapSource CreateDefaultIcon(double size)
    {
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            var center = new Point(size / 2, size / 2);
            var radius = size / 2 - 2;
            ctx.DrawEllipse(
                new SolidColorBrush(Color.FromRgb(0xDA, 0xA5, 0x20)), // Goldenrod
                new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)), 2), // Gold border
                center, radius, radius);

            // Draw a star/trophy shape hint
            var starBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xDC)); // Cornsilk
            var starSize = radius * 0.5;
            var formattedText = new FormattedText(
                "\u2605", // Star character
                System.Globalization.CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                starSize * 1.5,
                starBrush,
                VisualTreeHelper.GetDpi(visual).PixelsPerDip);
            ctx.DrawText(formattedText,
                new Point(center.X - formattedText.Width / 2, center.Y - formattedText.Height / 2));
        }

        var pixelSize = (int)Math.Max(1, size);
        var renderTarget = new RenderTargetBitmap(pixelSize, pixelSize, 96, 96, PixelFormats.Pbgra32);
        renderTarget.Render(visual);
        renderTarget.Freeze();
        return renderTarget;
    }

    private void SizeAndPosition(Rect gameWindowRect)
    {
        var notificationWidth = Math.Max(250, gameWindowRect.Width * WindowWidthFraction);
        var margin = Math.Min(gameWindowRect.Width, gameWindowRect.Height) * MarginFraction;
        _slideDistance = gameWindowRect.Height * SlideDistanceFraction;
        MaxWidth = notificationWidth;
        MinWidth = notificationWidth;

        // Measure to get actual height
        Measure(new Size(notificationWidth, double.PositiveInfinity));
        var actualHeight = DesiredSize.Height > 0 ? DesiredSize.Height : 80;

        Left = gameWindowRect.Right - notificationWidth - margin;
        Top = gameWindowRect.Bottom - actualHeight - margin - _slideDistance;
    }

    private void StartSlideIn()
    {
        // Slide up from below
        SlideTransform.Y = _slideDistance;

        var slideAnim = new DoubleAnimation(_slideDistance, 0, SlideDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        SlideTransform.BeginAnimation(TranslateTransform.YProperty, slideAnim);

        // Fade in
        var fadeInAnim = new DoubleAnimation(0, 1, SlideDuration);
        fadeInAnim.Completed += (_, _) => { if (!_recentMode) _holdTimer.Start(); };
        BeginAnimation(OpacityProperty, fadeInAnim);
    }

    private void StartFadeOut()
    {
        var fadeOutAnim = new DoubleAnimation(1, 0, FadeDuration);
        fadeOutAnim.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOutAnim);
    }
}
