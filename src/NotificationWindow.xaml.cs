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
    private const double SlideDistance = 60;

    private static readonly Duration SlideDuration = new(TimeSpan.FromMilliseconds(300));
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(500));
    private readonly DispatcherTimer _holdTimer;

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
    public void Show(string achievementName, string description, string? iconPath, Rect gameWindowRect)
    {
        AchievementName.Text = achievementName;
        AchievementDescription.Text = description;

        if (!string.IsNullOrEmpty(description))
        {
            AchievementDescription.Visibility = Visibility.Visible;
        }
        else
        {
            AchievementDescription.Visibility = Visibility.Collapsed;
        }

        LoadIcon(iconPath, gameWindowRect.Width);
        SizeAndPosition(gameWindowRect);
        Show();
        StartSlideIn();
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
        MaxWidth = notificationWidth;
        MinWidth = notificationWidth;

        // Measure to get actual height
        Measure(new Size(notificationWidth, double.PositiveInfinity));
        var actualHeight = DesiredSize.Height > 0 ? DesiredSize.Height : 80;

        Left = gameWindowRect.Right - notificationWidth - margin;
        Top = gameWindowRect.Bottom - actualHeight - margin - SlideDistance;
    }

    private void StartSlideIn()
    {
        // Slide up from below
        SlideTransform.Y = SlideDistance;

        var slideAnim = new DoubleAnimation(SlideDistance, 0, SlideDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        SlideTransform.BeginAnimation(TranslateTransform.YProperty, slideAnim);

        // Fade in
        var fadeInAnim = new DoubleAnimation(0, 1, SlideDuration);
        fadeInAnim.Completed += (_, _) => _holdTimer.Start();
        BeginAnimation(OpacityProperty, fadeInAnim);
    }

    private void StartFadeOut()
    {
        var fadeOutAnim = new DoubleAnimation(1, 0, FadeDuration);
        fadeOutAnim.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOutAnim);
    }
}
