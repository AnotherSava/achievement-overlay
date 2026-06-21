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
    private const double MarginFraction = 0.02;
    private const double SlideDistanceFraction = 0.015;
    // Fixed design width (DIU) at scale 1: padding 24 + icon 56 + icon margin 12 + text 230.
    // Every popup is this width (× scale), so notifications never vary in width.
    private const double BaseOuterWidth = 322;
    private const double IconBaseSize = 56;      // matches AchievementIcon Width/Height in XAML
    private const double MinScale = 0.75;
    private const double MaxScale = 2.5;

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

        var scale = ApplyScale();
        LoadIcon(iconPath, scale);
        SizeAndPosition(gameWindowRect, scale);
        Show();
        StartSlideIn();
    }

    /// <summary>
    /// Computes a uniform scale from the foreground display's logical width and applies it to the
    /// whole popup, so font, icon, padding and text-wrap width always keep the same proportions.
    /// Uses the foreground monitor's own DPI (not the primary's), so the popup is sized for the
    /// display it appears on.
    /// </summary>
    private double ApplyScale()
    {
        var targetWidth = Math.Max(250, AppUtilities.GetForegroundLogicalWidth() * WindowWidthFraction);
        var scale = Math.Clamp(targetWidth / BaseOuterWidth, MinScale, MaxScale);
        RootScale.ScaleX = scale;
        RootScale.ScaleY = scale;
        return scale;
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
        // Span the full popup width (no icon column) so the footer matches the achievement windows.
        AchievementName.MaxWidth = BaseOuterWidth;
        AchievementName.Width = BaseOuterWidth - 24; // minus RootBorder padding (12 each side)
        AchievementDescription.Visibility = Visibility.Collapsed;

        var scale = ApplyScale();
        PlaceRightAligned(gameWindowRect, scale, customTop, slideUpDistance);

        Show();
        StartSlideIn();
    }

    /// <summary>
    /// Measures the (already-scaled) content and places the window right-aligned to the game window
    /// at the given top, in recent/footer mode (no auto-dismiss).
    /// </summary>
    private void PlaceRightAligned(Rect gameWindowRect, double scale, double customTop, double slideUpDistance)
    {
        Left = RightAlignedLeft(gameWindowRect, scale, out _);
        Top = customTop;
        _slideDistance = slideUpDistance;
        _recentMode = true;
    }

    /// <summary>
    /// Left coordinate that right-aligns the popup within the game rect at the given scale, plus the
    /// edge margin used. Width is deterministic (BaseOuterWidth × scale): a pre-Show Measure on a
    /// Window returns 0, and the content is all fixed-width, so the rendered width is exactly this.
    /// Shared by the unlock popup and the recent cascade so their placement can never drift.
    /// </summary>
    private double RightAlignedLeft(Rect gameWindowRect, double scale, out double margin)
    {
        margin = Math.Min(gameWindowRect.Width, gameWindowRect.Height) * MarginFraction;
        return gameWindowRect.Right - BaseOuterWidth * scale - margin;
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

        var scale = ApplyScale();
        LoadIcon(iconPath, scale);
        PlaceRightAligned(gameWindowRect, scale, customTop, slideUpDistance);

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

    private void LoadIcon(string? iconPath, double scale)
    {
        // The icon box is a fixed design size (scaled by the root transform); decode/render the
        // source at the on-screen pixel size for crispness when scaled up.
        var renderSize = IconBaseSize * Math.Max(1.0, scale);

        if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = (int)renderSize;
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
        AchievementIcon.Source = CreateDefaultIcon(renderSize);
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

    private void SizeAndPosition(Rect gameWindowRect, double scale)
    {
        _slideDistance = gameWindowRect.Height * SlideDistanceFraction;
        Left = RightAlignedLeft(gameWindowRect, scale, out var margin);

        // Bottom-anchor: measure the content (RootBorder), not the Window — a pre-Show Window Measure
        // returns 0. RootBorder.DesiredSize includes the scale transform, matching the rect's DIPs.
        RootBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var height = RootBorder.DesiredSize.Height > 0 ? RootBorder.DesiredSize.Height : 80 * scale;
        Top = gameWindowRect.Bottom - height - margin - _slideDistance;
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
