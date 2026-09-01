using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
// The project enables WinForms implicit usings for the tray, so the WPF types this window uses have
// to be named explicitly wherever the two frameworks collide.
using WinFormsKeys = System.Windows.Forms.Keys;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using RadioButton = System.Windows.Controls.RadioButton;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace AchievementOverlay;

/// <summary>
/// What the settings window collected. The window writes nothing itself — the host persists the
/// changed config values and applies the one setting that doesn't live in config at all: the
/// Windows startup registry entry.
/// </summary>
public sealed class SettingsResult
{
    /// <summary>Config values the user actually changed, keyed by <see cref="SettingsData"/> property name.</summary>
    public required IReadOnlyDictionary<string, object?> ChangedSettings { get; init; }

    public required bool StartWithWindows { get; init; }
}

/// <summary>
/// The settings window: four pages behind a nav rail, one card per setting with its explanation
/// visible rather than hidden in a tooltip. WPF rather than WinForms because the design needs a
/// toggle, sliders and OS dark mode, none of which WinForms has — and the app already hosts WPF for
/// the notification itself. ThemeMode="System" supplies the Fluent control styles and the light/dark
/// switch; only the chrome the theme has no opinion about is styled here.
/// Pause notifications is deliberately absent: it is a momentary tray toggle, not a setting.
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>
    /// Families offered for the popup. A shortlist rather than every installed font: achievement
    /// text is localised, and a picker of everything invites one with no Cyrillic or CJK coverage.
    /// </summary>
    private static readonly string[] FontShortlist =
    {
        "Segoe UI", "Segoe UI Variable", "Arial", "Calibri", "Tahoma", "Verdana", "Times New Roman", "Consolas"
    };

    /// <summary>
    /// Widest the popup may be offered as a share of the screen. A third of the display is already a
    /// lot for a notification, and on a wide monitor the renderer's own clamp allows more than that.
    /// </summary>
    private const double MaxScreenPercent = 30;

    /// <summary>
    /// Colours offered as a single click. The first restores the shipped look, which is why it is
    /// labelled Default and why there is no separate Reset button — the way back is a visible control
    /// rather than one whose meaning has to be inferred. The last is light on purpose: the text
    /// colours are derived from the fill, so a pale popup is a real choice rather than a broken one.
    /// </summary>
    private static readonly (string Name, Color Colour)[] BackgroundPresets =
    {
        ("Default", Color.FromRgb(0x1A, 0x1A, 0x2E)),
        ("Charcoal", Color.FromRgb(0x1C, 0x1C, 0x1C)),
        ("Slate", Color.FromRgb(0x1E, 0x29, 0x3B)),
        ("Plum", Color.FromRgb(0x2A, 0x1B, 0x2E)),
        ("Paper", Color.FromRgb(0xF5, 0xF5, 0xF0))
    };

    private readonly SettingsData _current;

    /// <summary>
    /// Pairs each cell of the position grid to the value it stands for, so loading and collecting read
    /// the same table and cannot disagree about which cell means which corner.
    /// </summary>
    private readonly (RadioButton Cell, NotificationAnchor Anchor)[] _positionCells;

    /// <summary>The whole colour choice, colour and opacity together, as one value the way config holds it.</summary>
    private PopupBackground _background;
    private readonly List<Button> _presetSwatches = new();
    private Button _customSwatch = null!;
    private Color? _customColour;
    private readonly UnlockSoundPlayer? _soundPlayer;
    /// <summary>Previews still on screen, newest last — so repeated Show me clicks stack rather than overlap.</summary>
    private readonly List<NotificationWindow> _previews = new();
    /// <summary>Near edge of the first preview's slot — where a real unlock rests; the stack grows away from it.</summary>
    private double _previewEdge;
    /// <summary>The anchor the previews on screen were placed against, which a new choice invalidates.</summary>
    private NotificationAnchor _previewAnchor;
    private readonly List<string> _gameFolders;
    private readonly List<string> _savesFolders;
    private LowLevelKeyboardHook? _shortcutHook;
    private NotificationScale _lastScale;
    private bool _loaded;

    /// <summary>Set once the user saves; null while the window is open or after a cancel.</summary>
    public SettingsResult? Result { get; private set; }

    /// <param name="soundPlayer">
    /// The host's player, so "Show me" previews the sound through the same code the real unlock uses.
    /// </param>
    public SettingsWindow(AppConfig config, bool startWithWindows, IReadOnlyCollection<string> availableLanguages,
                          UnlockSoundPlayer? soundPlayer = null)
    {
        InitializeComponent();
        _soundPlayer = soundPlayer;
        _positionCells = new[]
        {
            (PosTopLeft, NotificationAnchor.TopLeft),
            (PosTopCenter, NotificationAnchor.TopCenter),
            (PosTopRight, NotificationAnchor.TopRight),
            (PosBottomLeft, NotificationAnchor.BottomLeft),
            (PosBottomCenter, NotificationAnchor.BottomCenter),
            (PosBottomRight, NotificationAnchor.BottomRight)
        };
        BuildBackgroundSwatches();

        // Snapshot: AppConfig replaces its settings object wholesale on every write, so this keeps
        // showing what the window was opened against.
        _current = config.GetCurrent();
        _gameFolders = AppConfig.SplitRawPaths(_current.GamesPaths).ToList();
        _savesFolders = AppConfig.SplitRawPaths(_current.GseSavesPaths).ToList();

        ApplyThemeBrushes();
        ClampToScreen();
        LoadWindowIcon();
        LoadValues(startWithWindows, availableLanguages);
        _loaded = true;
        UpdateScaleState();
        UpdateSoundState();
        UpdateBackgroundState();
        UpdateFooter();

        // The percent bounds and the footer's width both depend on the display, and in the
        // constructor this window is not on screen yet — GetForegroundLogicalWidth would answer for
        // whatever the user last had focused. Re-read once it is actually up.
        Loaded += (_, _) =>
        {
            ApplyScaleSliderRange(CollectScale().Unit);
            ScaleSlider.Value = _current.Scale.Value;
            UpdateScaleState();
        };
    }

    /// <summary>
    /// The window's height is set so Notifications — much the tallest page — fits without scrolling.
    /// On a screen too short for that (a laptop, or high display scaling) it is capped to the work
    /// area instead of opening off-screen, and the ScrollViewer does its job.
    /// </summary>
    private void ClampToScreen()
    {
        var available = SystemParameters.WorkArea.Height - 40;
        if (Height > available)
            Height = Math.Max(MinHeight, available);
    }

    /// <summary>WPF wants an ImageSource, so the embedded .ico is decoded rather than reused as a GDI icon.</summary>
    private void LoadWindowIcon()
    {
        try
        {
            using var stream = typeof(SettingsWindow).Assembly.GetManifestResourceStream("AchievementOverlay.icon.ico");
            if (stream != null)
                Icon = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not load the settings window icon: {ex.Message}");
        }
    }

    // --- Theme ---

    /// <summary>
    /// Supplies the handful of colours the Fluent theme has no key for — page, card and nav chrome —
    /// matched to whichever mode Windows is in. Everything else comes from ThemeMode="System".
    /// </summary>
    private void ApplyThemeBrushes()
    {
        var dark = IsSystemInDarkMode();
        void Set(string key, string hex) =>
            Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);

        Set("WindowBackground", dark ? "#202020" : "#F3F3F3");
        Set("PageBackground", dark ? "#272727" : "#FBFBFB");
        Set("CardBackground", dark ? "#2D2D2D" : "#FDFDFD");
        Set("CardBorder", dark ? "#3A3A3A" : "#E5E5E5");
        Set("NavSelected", dark ? "#333333" : "#EBEBEB");
        Set("NavHover", dark ? "#2F2F2F" : "#F0F0F0");
        Set("Accent", dark ? "#60CDFF" : "#005FB8");
        Set("SwitchOff", dark ? "#333333" : "#FFFFFF");
        Set("SwitchKnobOff", dark ? "#CCCCCC" : "#5D5D5D");
        Set("StatusGood", dark ? "#6CCB5F" : "#0F7B0F");
        Set("StatusWarn", dark ? "#FFC83D" : "#9A6A00");
    }

    private static bool IsSystemInDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            return false; // unreadable registry is not a reason to fail to open settings
        }
    }

    // --- Values ---

    private void LoadValues(bool startWithWindows, IReadOnlyCollection<string> availableLanguages)
    {
        StartWithWindowsToggle.IsChecked = startWithWindows;
        ShortcutBox.Text = _current.RecentAchievementsShortcut;
        RecentCountBox.Text = Math.Clamp(_current.RecentAchievementsCount, 1, 20).ToString();

        // english is always offered (it is the fallback), and so is whatever is configured now, so
        // opening the window can never silently drop a language the games happen not to declare.
        var languages = new SortedSet<string>(availableLanguages, StringComparer.OrdinalIgnoreCase) { "english" };
        if (!string.IsNullOrWhiteSpace(_current.Language))
            languages.Add(_current.Language);
        foreach (var language in languages)
            LanguageBox.Items.Add(language);
        LanguageBox.Text = _current.Language;

        var fonts = new List<string>(FontShortlist);
        var configuredFont = string.IsNullOrWhiteSpace(_current.Font) ? NotificationAppearance.DefaultFont : _current.Font.Trim();
        if (!fonts.Contains(configuredFont, StringComparer.OrdinalIgnoreCase))
            fonts.Insert(0, configuredFont);
        foreach (var font in fonts)
            FontBox.Items.Add(font);
        FontBox.Text = configuredFont;

        foreach (var (cell, anchor) in _positionCells)
            cell.IsChecked = anchor == _current.NotificationPosition;

        _background = _current.NotificationBackground;
        // The slider's unit is the alpha byte, not a percentage. 93 of the 154 alphas in range do not
        // survive a whole-percent round trip — the shipped 0xDD is one of them (221 → 87% → 222) — so a
        // percent slider would rewrite the user's colour every time settings were opened and saved.
        OpacitySlider.Minimum = PopupBackground.MinAlpha;
        OpacitySlider.Maximum = byte.MaxValue;
        OpacitySlider.Value = _background.A;

        var scale = _current.Scale;
        ScaleUnitBox.SelectedIndex = scale.Unit == ScaleUnit.ScreenPercent ? 0 : 1;
        ApplyScaleSliderRange(scale.Unit);
        ScaleSlider.Value = scale.Value;

        DurationSlider.Value = Math.Clamp(_current.DisplayDuration, DurationSlider.Minimum, DurationSlider.Maximum);
        SoundToggle.IsChecked = _current.SoundEnabled;
        // An empty soundPath is what "use the built-in sound" looks like in config.
        CustomSoundRadio.IsChecked = !string.IsNullOrWhiteSpace(_current.SoundPath);
        BuiltInSoundRadio.IsChecked = !CustomSoundRadio.IsChecked;
        SoundPathBox.Text = _current.SoundPath;
        GameOverlaySettingsToggle.IsChecked = _current.UseGameOverlaySettings;

        SteamKeyMasked.Password = _current.SteamWebApiKey ?? "";
        SteamKeyPlain.Text = _current.SteamWebApiKey ?? "";
        FirecrawlKeyBox.Text = _current.FirecrawlApiKey ?? "";

        RebuildFolderList(GameFoldersList, _gameFolders, DescribeGameFolder);
        RebuildFolderList(SavesFoldersList, _savesFolders, DescribeSavesFolder);
    }

    private SettingsData Collect() => new()
    {
        GamesPaths = string.Join(";", _gameFolders),
        GseSavesPaths = string.Join(";", _savesFolders),
        Language = LanguageBox.Text.Trim(),
        Font = FontBox.Text.Trim(),
        Scale = CollectScale(),
        NotificationPosition = CollectPosition(),
        NotificationBackground = _background,
        SoundEnabled = SoundToggle.IsChecked == true,
        // Selecting the built-in sound clears the path rather than remembering it, so config says
        // plainly which of the two is in use.
        SoundPath = CustomSoundRadio.IsChecked == true ? SoundPathBox.Text.Trim() : "",
        DisplayDuration = (int)DurationSlider.Value,
        UseGameOverlaySettings = GameOverlaySettingsToggle.IsChecked == true,
        RecentAchievementsShortcut = ShortcutBox.Text.Trim(),
        RecentAchievementsCount = ParseCount(),
        SteamWebApiKey = CurrentSteamKey().Trim(),
        FirecrawlApiKey = FirecrawlKeyBox.Text.Trim()
    };

    private string CurrentSteamKey() => SteamKeyPlain.IsVisible ? SteamKeyPlain.Text : SteamKeyMasked.Password;

    private int ParseCount() => int.TryParse(RecentCountBox.Text.Trim(), out var count) ? Math.Clamp(count, 1, 20) : _current.RecentAchievementsCount;

    // --- Navigation ---

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GeneralPage == null)
            return; // fires once during InitializeComponent, before the pages exist

        var index = Nav.SelectedIndex;
        GeneralPage.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        NotificationsPage.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        FoldersPage.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        AdvancedPage.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;

        // Show me and the size line belong to the notification settings only.
        var onNotifications = index == 1;
        ShowMeButton.Visibility = onNotifications ? Visibility.Visible : Visibility.Collapsed;
        PopupSizeText.Visibility = onNotifications ? Visibility.Visible : Visibility.Collapsed;
    }

    // --- Notifications page ---

    /// <summary>
    /// The checked cell. Falls back to what the window opened with rather than to the enum's default,
    /// so a grid that somehow has nothing checked reports no change instead of silently moving the
    /// popup to bottom-right on save.
    /// </summary>
    private NotificationAnchor CollectPosition()
    {
        foreach (var (cell, anchor) in _positionCells)
        {
            if (cell.IsChecked == true)
                return anchor;
        }

        return _current.NotificationPosition;
    }

    // --- Background colour ---

    /// <summary>
    /// Draws the preset swatches and the custom slot. Generated rather than written out in the XAML so
    /// the colours live in one table, next to the code that matches the current value against them.
    /// </summary>
    private void BuildBackgroundSwatches()
    {
        foreach (var (name, colour) in BackgroundPresets)
        {
            var swatch = NewSwatch(name);
            swatch.Background = new SolidColorBrush(colour);
            swatch.Click += (_, _) => ChooseBackgroundColour(colour);
            _presetSwatches.Add(swatch);
        }

        _customSwatch = NewSwatch("Custom colour");
        _customSwatch.Click += OnPickCustomBackground;
    }

    private Button NewSwatch(string name)
    {
        var swatch = new Button { Style = (Style)Resources["Swatch"], ToolTip = name };
        AutomationProperties.SetName(swatch, name);
        BackgroundSwatches.Children.Add(swatch);
        return swatch;
    }

    private void OnPickCustomBackground(object sender, RoutedEventArgs e)
    {
        var picked = DialogControls.PickColor(_background.ToColor());
        if (picked == null)
            return; // cancelled: the current choice stands

        ChooseBackgroundColour(picked.Value);
    }

    private void ChooseBackgroundColour(Color colour)
    {
        _background = _background.WithColour(colour);
        UpdateBackgroundState();
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_loaded)
            return;

        _background = _background.WithAlpha((byte)OpacitySlider.Value);
        UpdateBackgroundState();
    }

    /// <summary>
    /// Rings whichever swatch the current colour is, and keeps the custom slot showing it. Everything
    /// here is derived from <see cref="_background"/>, so the display cannot disagree with the value.
    /// </summary>
    private void UpdateBackgroundState()
    {
        if (!_loaded)
            return;

        var matched = false;
        for (var i = 0; i < BackgroundPresets.Length; i++)
        {
            var isCurrent = _background.IsColour(BackgroundPresets[i].Colour);
            _presetSwatches[i].Tag = isCurrent ? "selected" : null;
            matched |= isCurrent;
        }

        if (!matched)
            _customColour = Color.FromRgb(_background.R, _background.G, _background.B);

        _customSwatch.Background = _customColour is { } colour ? new SolidColorBrush(colour) : null;
        _customSwatch.Content = _customColour == null ? "+" : null;
        _customSwatch.Tag = matched ? null : "selected";

        OpacityValueText.Text = $"{Math.Round(OpacitySlider.Value / 2.55)}%";
    }

    private NotificationScale CollectScale() =>
        ScaleUnitBox.SelectedIndex == 0
            ? NotificationScale.ScreenPercent(ScaleSlider.Value)
            : NotificationScale.Pixels(ScaleSlider.Value);

    /// <summary>
    /// Switching unit re-ranges the slider and carries the current width across, so the popup doesn't
    /// jump: 15% of a 2560 px screen lands on 384 px rather than on a bare "15".
    /// </summary>
    private void OnScaleUnitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded)
            return;

        var displayWidth = AppUtilities.GetForegroundLogicalWidth();
        var currentWidth = NotificationWindow.ComputeWidth(_lastScale, displayWidth);
        var toPercent = ScaleUnitBox.SelectedIndex == 0;

        ApplyScaleSliderRange(toPercent ? ScaleUnit.ScreenPercent : ScaleUnit.Pixels);
        ScaleSlider.Value = toPercent ? currentWidth / displayWidth * 100.0 : currentWidth;
        UpdateScaleState();
    }

    /// <summary>
    /// Ranges the slider to exactly what the popup can actually draw, derived from the same
    /// readability clamp the renderer applies. Offering more is a lie: the popup saturates at
    /// 322 px × 2.5, so a slider that went to 50% of a 2560 px screen would draw 31.4% and silently
    /// disagree with its own label. The percent bounds depend on the display, so they are recomputed
    /// rather than hard-coded.
    /// </summary>
    private void ApplyScaleSliderRange(ScaleUnit unit)
    {
        var minWidth = NotificationScale.DesignWidth * NotificationScale.MinFactor;
        var maxWidth = NotificationScale.DesignWidth * NotificationScale.MaxFactor;

        if (unit == ScaleUnit.ScreenPercent)
        {
            // Whole percents, so snapping lands on 10, 11, 12 rather than on 9.5, 10.5, 11.5 — the
            // bounds are rounded inwards to keep every step inside what the popup can draw, then
            // held to MaxScreenPercent, which is tighter than the clamp allows on a wide display.
            var displayWidth = AppUtilities.GetForegroundLogicalWidth();
            var minPercent = Math.Ceiling(minWidth / displayWidth * 100);
            ScaleSlider.Minimum = minPercent;
            // Held at or above the floor: on a narrow or heavily scaled display the smallest popup
            // the renderer will draw already exceeds MaxScreenPercent — 1920x1080 at 200% scaling is
            // 960 logical px, where the design width alone is 34% — and a slider whose maximum sits
            // below its minimum snaps to nothing.
            ScaleSlider.Maximum = Math.Max(minPercent, Math.Min(MaxScreenPercent, Math.Floor(maxWidth / displayWidth * 100)));
            ScaleSlider.TickFrequency = 1;
        }
        else
        {
            ScaleSlider.Minimum = Math.Ceiling(minWidth);
            ScaleSlider.Maximum = Math.Floor(maxWidth);
            ScaleSlider.TickFrequency = 5;
        }
    }

    private void OnScaleValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateScaleState();

    private void UpdateScaleState()
    {
        if (!_loaded)
            return;

        _lastScale = CollectScale();
        ScaleValueText.Text = ScaleUnitBox.SelectedIndex == 0
            ? $"{ScaleSlider.Value:0.#}%"
            : $"{ScaleSlider.Value:0} px";
        UpdateFooter();
    }

    private void OnDurationChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_loaded)
            return;
        DurationText.Text = $"{(int)DurationSlider.Value} s";
        UpdateFooter();
    }

    /// <summary>
    /// States the popup's computed width and duration. Width is exact — the popup is always a fixed
    /// design width times the scale — so this is the same number the renderer will use, not a guess.
    /// </summary>
    private void UpdateFooter()
    {
        DurationText.Text = $"{(int)DurationSlider.Value} s";

        // The drawn width, after the readability clamp — so a slider value the popup would refuse is
        // visible here rather than only discovered on the next unlock.
        var width = (int)Math.Round(NotificationWindow.ComputeWidth(CollectScale(), AppUtilities.GetForegroundLogicalWidth()));
        PopupSizeText.Text = $"{width} px wide · {(int)DurationSlider.Value} s";
    }

    private void OnSoundToggled(object sender, RoutedEventArgs e) => UpdateSoundState();

    private void OnSoundChoiceChanged(object sender, RoutedEventArgs e) => UpdateSoundState();

    private void UpdateSoundState()
    {
        if (!_loaded)
            return;

        var soundOn = SoundToggle.IsChecked == true;
        SoundChoiceRows.IsEnabled = soundOn;
        var custom = CustomSoundRadio.IsChecked == true;
        SoundPathBox.IsEnabled = soundOn && custom;
        SoundBrowseButton.IsEnabled = soundOn && custom;
    }

    private void OnBrowseSound(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Wave audio (*.wav)|*.wav|All files (*.*)|*.*", CheckFileExists = true };
        var currentDir = Path.GetDirectoryName(SoundPathBox.Text.Trim());
        if (!string.IsNullOrEmpty(currentDir) && Directory.Exists(currentDir))
            dialog.InitialDirectory = currentDir;
        if (dialog.ShowDialog(this) == true)
            SoundPathBox.Text = dialog.FileName;
    }

    /// <summary>
    /// Fires a real popup with the settings as they stand. It needs no special handling: the
    /// notification is topmost so it draws over this window, and this window can be moved.
    /// </summary>
    private void OnShowMe(object sender, RoutedEventArgs e)
    {
        var edited = Collect();
        var width = NotificationWindow.ComputeWidth(edited.Scale, AppUtilities.GetForegroundLogicalWidth());

        // Popups of different widths can't stack into a tidy column — the aligned edge would stay put
        // but the other would step in and out — so a size change clears what's on screen first, and so
        // does a position change, which would otherwise leave a stack orphaned in the old corner.
        // Same width and place stacks instead, exactly as several unlocks arriving together do.
        if (_previews.Count > 0 && (Math.Abs(_previews[0].RenderedWidth - width) > 0.5 || edited.NotificationPosition != _previewAnchor))
            DismissPreviews();

        // Same order as a real unlock: sound first, then the popup.
        _soundPlayer?.Play(edited.SoundEnabled, edited.SoundPath);

        var window = new NotificationWindow(NotificationAppearance.From(edited));
        window.Closed += (_, _) =>
        {
            // Popups expire oldest-first, so it is the slot nearest the anchor that empties. Without
            // re-seating, the survivors would hang detached from the corner for the rest of their
            // duration — every toast stack closes that gap instead.
            _previews.Remove(window);
            LayoutPreviews(null);
        };
        window.ShowNotification(
            "Connoisseur",
            "Collect every bottle in the cellar.",
            EmbeddedAssets.GetConnoisseurIconPath(),
            AppUtilities.GetForegroundWindowRect());

        // ShowNotification has just placed this one where a real unlock goes, so when it is the first
        // it defines the edge the whole stack is built on.
        if (_previews.Count == 0)
        {
            _previewAnchor = edited.NotificationPosition;
            _previewEdge = NotificationPlacement.EdgeOf(_previewAnchor, window.Top, window.RenderedHeight);
        }

        _previews.Add(window); // newest last, and therefore furthest from the anchor
        LayoutPreviews(window);
    }

    /// <summary>
    /// Seats the whole stack on its edge, oldest nearest the anchor. One walk for both jobs — adding a
    /// popup and closing the gap a departed one left — because each slot's position depends on the
    /// height of the popup <em>in</em> it, and two separate formulations disagreed about which
    /// neighbour's height that was.
    /// </summary>
    /// <param name="newcomer">
    /// The popup being added, if any. It jumps to its slot; everyone else slides, and only if they
    /// actually move — sliding a window to where it already is plays a 220 ms animation of nothing.
    /// </param>
    private void LayoutPreviews(NotificationWindow? newcomer)
    {
        var edge = _previewEdge;
        foreach (var preview in _previews)
        {
            var top = NotificationPlacement.TopFor(_previewAnchor, edge, preview.RenderedHeight);
            if (ReferenceEquals(preview, newcomer))
                preview.Top = top;
            else if (Math.Abs(preview.Top - top) > 0.5)
                preview.SlideTo(top);

            edge = NotificationPlacement.Advance(_previewAnchor, edge, preview.RenderedHeight);
        }
    }

    private void DismissPreviews()
    {
        foreach (var preview in _previews.ToList())
        {
            try { preview.DismissImmediately(); }
            catch { /* already closing */ }
        }
        _previews.Clear();
    }

    // --- General page ---

    private void OnDigitsOnly(object sender, TextCompositionEventArgs e) => e.Handled = !e.Text.All(char.IsDigit);

    private void OnRecentCountUp(object sender, RoutedEventArgs e) => StepCount(1);

    private void OnRecentCountDown(object sender, RoutedEventArgs e) => StepCount(-1);

    private void StepCount(int delta) => RecentCountBox.Text = Math.Clamp(ParseCount() + delta, 1, 20).ToString();

    // --- Shortcut capture ---

    private void OnShortcutFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        _shortcutHook ??= new LowLevelKeyboardHook(OnHookedKeyDown);

    private void OnShortcutBlur(object sender, KeyboardFocusChangedEventArgs e) => EndShortcutCapture();

    private void EndShortcutCapture()
    {
        _shortcutHook?.Dispose();
        _shortcutHook = null;
    }

    /// <summary>
    /// Runs ahead of every hotkey owner, so a combination another app already holds — including an
    /// Explorer "Shortcut key" on a .lnk — is recorded here instead of running its owner.
    /// </summary>
    private bool OnHookedKeyDown(WinFormsKeys key)
    {
        // The hook is system-wide: stay out of the way unless this window is genuinely active with
        // the field focused, or alt-tabbing away would swallow another app's keys.
        if (!IsActive || !ShortcutBox.IsKeyboardFocusWithin)
            return false;

        return TryCaptureShortcut(key,
            ctrl: LowLevelKeyboardHook.IsKeyDown(WinFormsKeys.ControlKey),
            shift: LowLevelKeyboardHook.IsKeyDown(WinFormsKeys.ShiftKey),
            alt: LowLevelKeyboardHook.IsKeyDown(WinFormsKeys.Menu),
            win: LowLevelKeyboardHook.IsKeyDown(WinFormsKeys.LWin) || LowLevelKeyboardHook.IsKeyDown(WinFormsKeys.RWin));
    }

    /// <summary>Fallback for the rare case the hook could not be installed.</summary>
    private void OnShortcutKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_shortcutHook?.IsInstalled == true)
            return; // the hook already consumed anything capturable

        var key = (WinFormsKeys)KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key);
        var modifiers = Keyboard.Modifiers;
        if (TryCaptureShortcut(key,
                ctrl: modifiers.HasFlag(ModifierKeys.Control),
                shift: modifiers.HasFlag(ModifierKeys.Shift),
                alt: modifiers.HasFlag(ModifierKeys.Alt),
                win: modifiers.HasFlag(ModifierKeys.Windows)))
            e.Handled = true;
    }

    /// <summary>
    /// The single place a keystroke becomes a shortcut string, shared by the hook and the fallback so
    /// the two can't disagree. Capturing rather than trusting typed text is what guarantees the value
    /// round-trips through <see cref="GlobalHotkey.ParseHotkeyString"/>.
    /// </summary>
    private bool TryCaptureShortcut(WinFormsKeys key, bool ctrl, bool shift, bool alt, bool win)
    {
        // Leave window navigation alone, or Save/Cancel become unreachable while this field has focus.
        if (key is WinFormsKeys.Enter or WinFormsKeys.Escape or WinFormsKeys.Tab)
            return false;

        // A modifier on its own — wait for the key it modifies. The hook reports the physical side
        // (LControlKey), the WPF event the generic one, so both have to be listed.
        if (key is WinFormsKeys.None or WinFormsKeys.ControlKey or WinFormsKeys.ShiftKey or WinFormsKeys.Menu
            or WinFormsKeys.LControlKey or WinFormsKeys.RControlKey or WinFormsKeys.LShiftKey or WinFormsKeys.RShiftKey
            or WinFormsKeys.LMenu or WinFormsKeys.RMenu or WinFormsKeys.LWin or WinFormsKeys.RWin)
            return false;

        if (key is WinFormsKeys.Back or WinFormsKeys.Delete)
        {
            ShortcutBox.Text = ""; // no global shortcut; the tray menu still opens the panel
            return true;
        }

        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (shift) parts.Add("Shift");
        if (alt) parts.Add("Alt");
        if (win) parts.Add("Win");
        parts.Add(key.ToString());
        ShortcutBox.Text = string.Join("+", parts);
        return true;
    }

    // --- Folders page ---

    private void OnAddGameFolder(object sender, RoutedEventArgs e) => AddFolder(_gameFolders, GameFoldersList, DescribeGameFolder);

    private void OnAddSavesFolder(object sender, RoutedEventArgs e) => AddFolder(_savesFolders, SavesFoldersList, DescribeSavesFolder);

    private void AddFolder(List<string> folders, ItemsControl list, Func<string, (string Text, string BrushKey)> describe)
    {
        var last = folders.Count > 0 ? AppConfig.ExpandEnvironmentVariables(folders[^1]) : null;
        var picked = DialogControls.PickFolder(null, last);
        if (picked == null)
            return;

        // Packed back into variable form so a picked AppData folder stays portable between machines.
        var stored = AppConfig.CollapseEnvironmentVariables(picked);
        if (folders.Any(f => string.Equals(AppConfig.ExpandEnvironmentVariables(f), picked, StringComparison.OrdinalIgnoreCase)))
            return;

        folders.Add(stored);
        RebuildFolderList(list, folders, describe);
    }

    /// <summary>
    /// Draws one card per folder: the path as stored, what checking it right now says about it, and
    /// Change/Remove. The status is the check that used to run only when the dialog was closed.
    /// </summary>
    private void RebuildFolderList(ItemsControl list, List<string> folders, Func<string, (string Text, string BrushKey)> describe)
    {
        list.Items.Clear();
        for (var i = 0; i < folders.Count; i++)
        {
            var index = i;
            var (statusText, brushKey) = describe(folders[i]);

            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = folders[i], FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = statusText, FontSize = 12, Margin = new Thickness(0, 2, 0, 0), Foreground = (Brush)Resources[brushKey] });

            var change = new Button { Content = "Change", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0) };
            change.Click += (_, _) => ChangeFolder(folders, index, list, describe);
            var remove = new Button { Content = "Remove", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
            remove.Click += (_, _) => { folders.RemoveAt(index); RebuildFolderList(list, folders, describe); };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(change);
            buttons.Children.Add(remove);

            var row = new DockPanel();
            DockPanel.SetDock(buttons, Dock.Right);
            row.Children.Add(buttons);
            row.Children.Add(text);

            list.Items.Add(new Border
            {
                Style = (Style)Resources["Card"],
                Padding = new Thickness(14, 10, 12, 10),
                Child = row
            });
        }
    }

    private void ChangeFolder(List<string> folders, int index, ItemsControl list, Func<string, (string Text, string BrushKey)> describe)
    {
        var picked = DialogControls.PickFolder(null, AppConfig.ExpandEnvironmentVariables(folders[index]));
        if (picked == null)
            return;

        folders[index] = AppConfig.CollapseEnvironmentVariables(picked);
        RebuildFolderList(list, folders, describe);
    }

    private static (string Text, string BrushKey) DescribeGameFolder(string raw)
    {
        var path = AppConfig.ExpandEnvironmentVariables(raw);
        if (!Directory.Exists(path))
            return ("drive not connected — kept, skipped for now", "StatusWarn");

        try
        {
            var count = Directory.EnumerateFiles(path, "steam_appid.txt", AppUtilities.RecursiveScan).Count();
            return count == 0
                ? ("no games with achievement metadata found", "StatusWarn")
                : ($"{count} game{(count == 1 ? "" : "s")} with achievement metadata", "StatusGood");
        }
        catch (Exception ex)
        {
            return ($"could not be scanned: {ex.Message}", "StatusWarn");
        }
    }

    private static (string Text, string BrushKey) DescribeSavesFolder(string raw)
    {
        var path = AppConfig.ExpandEnvironmentVariables(raw);
        if (!Directory.Exists(path))
            return ("not found — nothing will be watched here", "StatusWarn");

        try
        {
            var count = Directory.GetDirectories(path).Length;
            return ($"found · {count} game folder{(count == 1 ? "" : "s")} inside", "StatusGood");
        }
        catch (Exception ex)
        {
            return ($"could not be read: {ex.Message}", "StatusWarn");
        }
    }

    // --- Advanced page ---

    private void OnToggleSteamKeyReveal(object sender, MouseButtonEventArgs e)
    {
        var reveal = !SteamKeyPlain.IsVisible;
        if (reveal)
            SteamKeyPlain.Text = SteamKeyMasked.Password;
        else
            SteamKeyMasked.Password = SteamKeyPlain.Text;

        SteamKeyPlain.Visibility = reveal ? Visibility.Visible : Visibility.Collapsed;
        SteamKeyMasked.Visibility = reveal ? Visibility.Collapsed : Visibility.Visible;
        SteamKeyReveal.Text = reveal ? "Hide" : "Show";
    }

    // --- Save ---

    /// <summary>
    /// Returns the first entry that would leave the app broken or silently doing nothing, along with
    /// the page it lives on. Values that merely warn at runtime (a games folder that isn't mounted)
    /// are left alone — the folder card already says so.
    /// </summary>
    private (int Page, string Message)? FindProblem()
    {
        var savesPaths = AppConfig.ParseGamesPaths(string.Join(";", _savesFolders));
        if (savesPaths.Length == 0)
            return (2, "Add at least one GSE Saves folder — the app has nothing to watch without one, and won't start.");
        if (!savesPaths.Any(Directory.Exists))
            return (2, "None of these GSE Saves folders exist:\r\n\r\n" + string.Join("\r\n", savesPaths));

        if (SoundToggle.IsChecked == true && CustomSoundRadio.IsChecked == true)
        {
            // Checked as typed, not expanded: the sound player opens the raw path, so accepting a
            // value that only resolves after expansion would fail silently at the first unlock.
            var soundPath = SoundPathBox.Text.Trim();
            if (soundPath.Length == 0)
                return (1, "Choose a sound file, or select the built-in sound.");
            if (!File.Exists(soundPath))
                return (1, $"Sound file not found:\r\n\r\n{soundPath}\r\n\r\nSelect the built-in sound to use the default.");
        }

        return null;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var problem = FindProblem();
        if (problem != null)
        {
            Nav.SelectedIndex = problem.Value.Page; // take the user to the page that needs fixing
            MessageBox.Show(this, problem.Value.Message, "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new SettingsResult
        {
            ChangedSettings = SettingsDiff.Compute(_current, Collect()),
            StartWithWindows = StartWithWindowsToggle.IsChecked == true
        };
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    protected override void OnClosed(EventArgs e)
    {
        // Closing while the field still has focus doesn't always raise the blur, and a stray
        // system-wide hook would keep filtering every keystroke on the machine.
        EndShortcutCapture();
        base.OnClosed(e);
    }
}
