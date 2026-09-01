using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AchievementOverlay;

public partial class NotificationWindow : Window
{
    // Fixed design width (DIU) at scale 1: padding 24 + icon 56 + icon margin 12 + text 230.
    // Every popup is this width (× scale), so notifications never vary in width.
    private const double BaseOuterWidth = NotificationScale.DesignWidth;
    private const double IconBaseSize = 56;      // matches AchievementIcon Width/Height in XAML
    private const double MinScale = NotificationScale.MinFactor;
    private const double MaxScale = NotificationScale.MaxFactor;

    /// <summary>Which edge this popup is drawn against.</summary>
    private NotificationAnchor Anchor => _appearance.Anchor;

    private static readonly Duration SlideToDuration = new(TimeSpan.FromMilliseconds(220));
    private double _slideTarget;

    /// <summary>
    /// Slides the popup to a new top rather than jumping there — used when a stack closes a gap after
    /// one of its members expires. Nothing the user did moved it, so an instant jump reads as a glitch.
    /// Animates Window.Top itself: a transform would only move the content inside the window.
    /// </summary>
    public void SlideTo(double top)
    {
        // Read before the animation takes the property over, or this asks the animation where it is.
        var falling = top > Top;
        _slideTarget = top;
        // Falling, it is under constant acceleration from rest: s = ½at² is displacement ∝ t², which
        // is exactly a quadratic ease-in. It arrives at full speed and stops, the way a dropped thing
        // lands — an ease-out would start fast and glide, which reads as sliding, not falling. Rising,
        // which is what closing a gap means at a top anchor, that same acceleration reads as a glitch,
        // so the ease is taken from the direction rather than assumed.
        var animation = new DoubleAnimation(top, SlideToDuration)
        {
            EasingFunction = new QuadraticEase { EasingMode = falling ? EasingMode.EaseIn : EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            // A held animation would make every later assignment to Top silently do nothing, so hand
            // the property back — unless a newer slide has already taken over, which will finish it.
            if (_slideTarget != top)
                return;
            BeginAnimation(TopProperty, null);
            Top = top;
        };
        BeginAnimation(TopProperty, animation);
    }

    /// <summary>
    /// Outer size as drawn, set by <see cref="ShowNotification"/> when it places a single popup.
    /// Both stay 0 for a window shown through <see cref="ShowRecent"/> or <see cref="ShowFooter"/>:
    /// those are placed at a top their caller has already worked out, so nothing here measures them,
    /// and the caller reads <c>ActualHeight</c> after the render instead.
    /// </summary>
    public double RenderedHeight { get; private set; }
    public double RenderedWidth { get; private set; }

    private static readonly Duration SlideDuration = new(TimeSpan.FromMilliseconds(300));
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(500));
    private readonly DispatcherTimer _holdTimer;
    /// <summary>Signed: away from the anchored edge, so it carries the direction as well as the distance.</summary>
    private double _slideOffset;
    private bool _recentMode;

    private readonly NotificationAppearance _appearance;
    private readonly PopupPalette _palette;

    public NotificationWindow(NotificationAppearance appearance)
    {
        InitializeComponent();
        _appearance = appearance;
        Opacity = 0;
        // The fill is the user's; every foreground is derived from it, so no choice of colour can
        // leave the text unreadable.
        _palette = PopupPalette.For(appearance.Background.ToColor());
        ApplyPalette();
        // Set on the window so every TextBlock inherits it — one assignment, no per-element drift.
        // An unknown family is not an error: WPF falls back rather than rendering nothing. A font
        // file the game supplies wins when it loads; when it doesn't, the configured family is a
        // better answer than no text.
        FontFamily = PopupFontLoader.Load(appearance.FontFilePath) ?? new FontFamily(appearance.ResolvedFont);
        _holdTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(appearance.DurationSeconds) };
        _holdTimer.Tick += (_, _) =>
        {
            _holdTimer.Stop();
            StartFadeOut();
        };
    }

    /// <summary>
    /// Paints the fill and every foreground. Done here rather than in the XAML because the recent
    /// panel's dismiss hint is set in code, and a XAML-only version would leave that one line behind.
    /// </summary>
    private void ApplyPalette()
    {
        RootBorder.Background = Frozen(_palette.Background);
        AchievementName.Foreground = Frozen(_palette.Title);
        AchievementDescription.Foreground = Frozen(_palette.Description);
        GameInfoText.Foreground = Frozen(_palette.GameLine);
    }

    private static SolidColorBrush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Shows the notification window at the configured corner or edge of the given rectangle.
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

        var scale = ApplyScale(gameWindowRect);
        LoadIcon(iconPath, scale);
        SizeAndPosition(gameWindowRect, scale);
        Show();
        StartSlideIn();
    }

    /// <summary>
    /// Applies a uniform scale to the whole popup, so font, icon, padding and text-wrap width always
    /// keep the same proportions. A share of the screen is taken from the rect the popup is being
    /// placed in — that rect is already this monitor's work area in its own DPI's units, so the popup
    /// is sized for the display it appears on; a fixed setting uses the width as given.
    /// </summary>
    private double ApplyScale(Rect area)
    {
        var scale = ComputeScale(_appearance.Scale, area.Width);
        RootScale.ScaleX = scale;
        RootScale.ScaleY = scale;
        return scale;
    }

    /// <summary>
    /// The scale factor for a setting on a display of the given logical width. Pure, so the width the
    /// settings window reports and the width actually drawn come from the same calculation.
    /// </summary>
    internal static double ComputeScale(NotificationScale setting, double displayLogicalWidth) =>
        Math.Clamp(setting.WidthOn(displayLogicalWidth) / BaseOuterWidth, MinScale, MaxScale);

    /// <summary>The popup's drawn width for a setting — what the settings window's footer states.</summary>
    internal static double ComputeWidth(NotificationScale setting, double displayLogicalWidth) =>
        BaseOuterWidth * ComputeScale(setting, displayLogicalWidth);

    /// <summary>
    /// Shows as a footer info bar — no icon, no title, just text. No auto-dismiss.
    /// </summary>
    public void ShowFooter(string text, Rect gameWindowRect, double customTop, double slideOffset)
    {
        AchievementIcon.Visibility = Visibility.Collapsed;
        AchievementName.Text = text;
        AchievementName.FontWeight = FontWeights.Normal;
        AchievementName.FontSize = 11;
        AchievementName.Foreground = Frozen(_palette.Footer);
        AchievementName.TextAlignment = TextAlignment.Center;
        // Span the full popup width (no icon column) so the footer matches the achievement windows.
        AchievementName.MaxWidth = BaseOuterWidth;
        AchievementName.Width = BaseOuterWidth - 24; // minus RootBorder padding (12 each side)
        AchievementDescription.Visibility = Visibility.Collapsed;

        var scale = ApplyScale(gameWindowRect);
        PlaceForStack(gameWindowRect, scale, customTop, slideOffset);

        Show();
        StartSlideIn();
    }

    /// <summary>
    /// Places the window as one member of a stack: aligned to the anchored edge, at a top the caller
    /// has worked out from the running edge. Width is deterministic (BaseOuterWidth × scale) — a
    /// pre-Show Measure on a Window returns 0, and the content is all fixed-width, so the rendered
    /// width is exactly this.
    /// </summary>
    /// <remarks>
    /// The <c>_recentMode</c> assignment is a lifetime decision rather than a placement one: it is the
    /// only thing that stops the hold timer starting, so every popup placed through here stays on
    /// screen until it is dismissed.
    /// </remarks>
    private void PlaceForStack(Rect area, double scale, double top, double slideOffset)
    {
        Left = NotificationPlacement.LeftFor(Anchor, area, BaseOuterWidth * scale);
        Top = top;
        _slideOffset = slideOffset;
        _recentMode = true;
    }

    /// <summary>
    /// Shows as a "recent" notification — custom slide distance, extra text lines, no auto-dismiss.
    /// </summary>
    public void ShowRecent(string achievementName, string description, string? iconPath, Rect gameWindowRect, double customTop, double slideOffset, string? gameInfoLine)
    {
        AchievementName.Text = achievementName;
        AchievementDescription.Text = description;
        AchievementDescription.Visibility = string.IsNullOrEmpty(description) ? Visibility.Collapsed : Visibility.Visible;

        if (!string.IsNullOrEmpty(gameInfoLine))
        {
            GameInfoText.Text = gameInfoLine;
            GameInfoText.Visibility = Visibility.Visible;
        }

        var scale = ApplyScale(gameWindowRect);
        LoadIcon(iconPath, scale);
        PlaceForStack(gameWindowRect, scale, customTop, slideOffset);

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
        AchievementIcon.Source = CreateDefaultIcon(renderSize, _palette.IconRing);
    }

    private static BitmapSource CreateDefaultIcon(double size, Color ring)
    {
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            var center = new Point(size / 2, size / 2);
            var radius = size / 2 - 2;
            ctx.DrawEllipse(
                new SolidColorBrush(Color.FromRgb(0xDA, 0xA5, 0x20)), // Goldenrod
                new Pen(new SolidColorBrush(ring), 2), // gold, or the ink when gold would vanish into the fill
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

    private void SizeAndPosition(Rect area, double scale)
    {
        // The height is needed before placing, so measure the content (RootBorder) rather than the
        // Window — a pre-Show Window Measure returns 0. RootBorder.DesiredSize includes the scale
        // transform, matching the rect's DIPs.
        RootBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        RenderedHeight = RootBorder.DesiredSize.Height > 0 ? RootBorder.DesiredSize.Height : 80 * scale;
        RenderedWidth = BaseOuterWidth * scale;

        var placement = NotificationPlacement.Place(Anchor, area, RenderedWidth, RenderedHeight);
        Left = placement.Left;
        Top = placement.Top;
        _slideOffset = placement.SlideOffset;
    }

    private void StartSlideIn()
    {
        // Starts flush with the anchored edge and settles inward, whichever edge that is.
        SlideTransform.Y = _slideOffset;

        var slideAnim = new DoubleAnimation(_slideOffset, 0, SlideDuration)
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
