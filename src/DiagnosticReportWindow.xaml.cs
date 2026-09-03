using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;

namespace AchievementOverlay;

/// <summary>
/// One game the report can be built for. Games with no <see cref="GameInfo"/> are listed too: a game
/// tracked through a self-describing unlock file has no <c>steam_settings</c> at all, and it is
/// exactly the configuration most likely to be the subject of a report.
/// </summary>
public sealed class DiagnosticGameChoice
{
    public required string AppId { get; init; }
    public GameInfo? Game { get; init; }

    public override string ToString() =>
        string.IsNullOrWhiteSpace(Game?.GameName) ? AppId : $"{Game!.GameName} ({AppId})";
}

/// <summary>
/// Shows the diagnostic report for one game so the user can read it before sending it anywhere, then
/// saves it as a single JSON file to attach to an issue.
/// <para>
/// The report is split into parts reached from a nav rail, because the review is the only thing
/// standing between the user and publishing their folder layout — and one pane holding all of it is
/// one nobody reads to the end. Each part can be left out, and the pane always shows exactly what the
/// saved file will contain, being sliced out of that document rather than built alongside it, so what
/// is reviewed and what is sent cannot drift apart.
/// </para>
/// </summary>
public sealed partial class DiagnosticReportWindow : Window
{
    /// <summary>One part of the report: what it is called, where it lives in the document, and whether it is going.</summary>
    private sealed class ReportSection
    {
        public required string Title { get; init; }

        /// <summary>
        /// The top-level keys of the report this part shows. Usually one; the first shows two, and
        /// renders them under their own names so the pane stays a faithful slice of the document.
        /// </summary>
        public required string[] Keys { get; init; }

        /// <summary>False for the part that is always sent, which therefore offers no control to change it.</summary>
        public required bool Optional { get; init; }

        public required TextBlock Label { get; init; }
        public required TextBlock Chip { get; init; }
        public required ListBoxItem Row { get; init; }

        public bool Included { get; set; } = true;
        public string Description { get; set; } = "";
        public string Pane { get; set; } = "";
    }

    private readonly string[] _gseSavesPaths;
    private readonly string[] _gamesPaths;
    private readonly List<ReportSection> _sections;

    /// <summary>
    /// Set once the sections exist. Both handlers below run during InitializeComponent — the rail's
    /// SelectedIndex="0" raises SelectionChanged while the XAML is still being parsed — and would
    /// otherwise read fields that are not there yet.
    /// </summary>
    private readonly bool _loaded;

    private string _reportText = "";

    public DiagnosticReportWindow(IReadOnlyList<DiagnosticGameChoice> choices, string[] gseSavesPaths, string[] gamesPaths)
    {
        _gseSavesPaths = gseSavesPaths;
        _gamesPaths = gamesPaths;

        InitializeComponent();
        DialogChrome.ApplyThemeBrushes(Resources);
        DialogChrome.ClampToScreen(this);
        DialogChrome.LoadWindowIcon(this);

        _sections = new List<ReportSection>
        {
            new() { Title = "App and game", Optional = false, Keys = new[] { "app", "game" }, Label = Label0, Chip = Chip0, Row = Row0 },
            new() { Title = "App config", Optional = true, Keys = new[] { "config" }, Label = Label1, Chip = Chip1, Row = Row1 },
            new() { Title = "Log", Optional = true, Keys = new[] { "log" }, Label = Label2, Chip = Chip2, Row = Row2 },
            new() { Title = "Achievement schema", Optional = true, Keys = new[] { "schema" }, Label = Label3, Chip = Chip3, Row = Row3 },
            new() { Title = "Save file", Optional = true, Keys = new[] { "unlockFile" }, Label = Label4, Chip = Chip4, Row = Row4 }
        };

        foreach (var section in _sections)
        {
            section.Label.Text = section.Title;
            // Without this a screen reader reads the row as "System.Windows.Controls.ListBoxItem":
            // the caption lives in a DockPanel the row cannot name itself from.
            System.Windows.Automation.AutomationProperties.SetName(section.Row, section.Title);
        }

        foreach (var choice in choices)
            GameBox.Items.Add(choice);

        _loaded = true;

        if (GameBox.Items.Count > 0)
        {
            GameBox.SelectedIndex = 0;
        }
        else
        {
            Pane.Text = "No games found. Add a game, or run one once so its GSE Saves folder appears, then try again.";
            ShowSection(_sections[0]);
        }
    }

    private ReportSection Current => _sections[Math.Max(Nav.SelectedIndex, 0)];

    private void OnGameChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loaded)
            Rebuild();
    }

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loaded)
            ShowSection(Current);
    }

    /// <summary>Note both events: wiring only Checked would make unticking — the whole point of the control — do nothing.</summary>
    private void OnIncludeChanged(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
            return;

        Current.Included = IncludeToggle.IsChecked == true;
        Rebuild();
    }

    /// <summary>Writes the page from one part: its heading, whether it is going, its summary and its pane.</summary>
    private void ShowSection(ReportSection section)
    {
        PageTitleText.Text = section.Title;
        PageIntroText.Text = section.Description;
        IncludeToggle.Visibility = section.Optional ? Visibility.Visible : Visibility.Collapsed;
        IncludeToggle.IsChecked = section.Included;
        IncludeLabel.Text = section.Optional ? "Include" : "Always included";
        Pane.Text = section.Pane;
        Pane.ScrollToHome();
    }

    private DiagnosticSections Chosen() => new()
    {
        Config = _sections[1].Included,
        Log = _sections[2].Included,
        Schema = _sections[3].Included,
        Unlock = _sections[4].Included
    };

    private void Rebuild()
    {
        if (GameBox.SelectedItem is not DiagnosticGameChoice choice)
            return;

        try
        {
            _reportText = DiagnosticReport.Compose(
                DiagnosticReport.Collect(choice.AppId, choice.Game, _gseSavesPaths, _gamesPaths), Chosen());
        }
        catch (Exception ex)
        {
            _reportText = "";
            Logger.Error($"Could not build the diagnostic report for appid {choice.AppId}: {ex.Message}");
        }

        // Each pane is cut out of the saved document rather than rendered beside it, so what is on
        // screen is what leaves the machine.
        var report = _reportText.Length > 0 ? JsonNode.Parse(_reportText) : null;

        foreach (var section in _sections)
        {
            var node = Slice(report, section.Keys);
            section.Pane = node == null
                ? "Could not build the report — see overlay.log."
                : node.ToJsonString(DiagnosticReport.ReportJson).ReplaceLineEndings();
            section.Description = Describe(section.Title, section.Included, node);

            // Two marks, not one: the strike-through is invisible to a screen reader and easy to miss
            // at a glance, and the chip alone reads as decoration.
            section.Label.TextDecorations = section.Included ? null : TextDecorations.Strikethrough;
            section.Chip.Visibility = section.Included ? Visibility.Collapsed : Visibility.Visible;
            System.Windows.Automation.AutomationProperties.SetName(
                section.Row, section.Included ? section.Title : $"{section.Title}, left out");
        }

        ShowSection(Current);

        var usable = _reportText.Length > 0;
        CopyButton.IsEnabled = usable;
        SaveButton.IsEnabled = usable;
        FooterStatus.Text = usable ? "" : "The report could not be built — see overlay.log.";
    }

    /// <summary>
    /// The part of the report a pane shows. One key gives that key's value; several give an object of
    /// those keys, which is still exactly what the saved file holds under them — the pane never
    /// renders anything the document does not.
    /// </summary>
    private static JsonNode? Slice(JsonNode? report, string[] keys)
    {
        if (report == null)
            return null;
        if (keys.Length == 1)
            return report[keys[0]];

        var slice = new JsonObject();
        foreach (var key in keys)
            slice[key] = report[key]?.DeepClone();
        return slice;
    }

    /// <summary>
    /// The one line of text a part gets. It is computed rather than written down beside a separate
    /// summary, because the two used to say the same things in different words: an intro claiming
    /// "API keys are replaced before you see them" over a summary reading "API keys hidden". Here the
    /// description and this part's live figures are one sentence, so there is nothing to keep in sync.
    /// </summary>
    private static string Describe(string title, bool included, JsonNode? node)
    {
        if (node == null)
            return "";
        if (!included)
            return "Left out. The report will record that you chose not to send it, rather than simply not have it.";

        switch ((string?)node["status"])
        {
            case "missing": return "That file is not there.";
            case "unreadable": return "That file could not be read.";
            case "unparsable": return "That file is not valid JSON. The report carries the start of it, so what is wrong with it is visible.";
            case "not configured": return "This game has none.";
        }

        switch (title)
        {
            case "App and game":
                var dirs = node["game"]?["settingsDirs"]?.AsArray().Count ?? 0;
                return $"The app version and the build it came from, and the {dirs} steam_settings folder{(dirs == 1 ? "" : "s")} found for this game.";
            case "App config":
                var settings = node["content"]?.AsObject().Count ?? 0;
                return $"Your {settings} settings as the app reads them, with any API key replaced by {DiagnosticReport.Redacted}.";
            case "Log":
                var sessions = node["sessions"]?.AsArray().Count ?? 0;
                var lines = node["sessions"]?.AsArray().Sum(s => s?["lines"]?.AsArray().Count ?? 0) ?? 0;
                var dropped = (int?)node["linesAboutOtherGamesRemoved"] ?? 0;
                var removed = dropped > 0
                    ? $", and {dropped} line{(dropped == 1 ? "" : "s")} about your other games removed"
                    : "";
                return $"The last {sessions} run{(sessions == 1 ? "" : "s")} of the app: {lines} lines, narrowed to this game{removed}.";
            case "Achievement schema":
                var achievements = node["content"]?.AsArray().Count ?? 0;
                return $"The game's {achievements} achievement definitions, where its names, text and icons come from.";
            case "Save file":
                var entries = node["content"]?.AsObject().Count ?? 0;
                return $"The emulator's record of what you have unlocked: {entries} entries.";
            default:
                return "";
        }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_reportText.Length == 0)
            return;

        try
        {
            // WinForms' Clipboard retries when another process holds it; WPF's throws. A failed copy
            // must not take the tray down with it.
            System.Windows.Forms.Clipboard.SetText(_reportText);
            FooterStatus.Text = "Copied to the clipboard.";
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not copy the diagnostic report: {ex.Message}");
            FooterStatus.Text = "Could not copy — another program is holding the clipboard.";
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_reportText.Length == 0 || GameBox.SelectedItem is not DiagnosticGameChoice choice)
            return;

        // Not IDisposable, unlike the WinForms one: 'using' here would not compile.
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = DiagnosticReport.SuggestedFileName(choice.AppId, choice.Game?.GameName),
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            File.WriteAllText(dialog.FileName, _reportText);
            Logger.Info($"Saved diagnostic report for appid {choice.AppId} to '{dialog.FileName}'.");
            FooterStatus.Text = $"Saved to {dialog.FileName}";
            Process.Start("explorer.exe", $"/select,\"{dialog.FileName}\"");
        }
        catch (Exception ex)
        {
            Logger.Error($"Could not save the diagnostic report: {ex.Message}");
            // Qualified: WinForms is in global scope here, and both namespaces have a MessageBox.
            System.Windows.MessageBox.Show(this, $"Could not save the report:\r\n\r\n{ex.Message}",
                "Achievement Overlay", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
