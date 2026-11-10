using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using AchievementOverlay.GbeConfig;

namespace AchievementOverlay;

/// <summary>
/// Wizard that walks the user through adding a game: it asks for the game folder, then
/// only asks for the AppID / API key when they can't be detected or aren't already
/// stored, collects a few options, and finally runs the GBE config generator with live
/// progress. On success it invokes a callback so the host can register the new game.
/// </summary>
public sealed class AddGameForm : Form, IConfigProgress
{
    private enum Page { Folder, AppId, ApiKey, Firecrawl, Ready, Progress }

    private const int FormWidth = 560;
    private const int HelpWidth = FormWidth - 40;
    // The progress log opens below the buttons, so the steps above it stay put. Its lines quote full game paths,
    // which is why the dialog widens while it is shown.
    private const int DetailsWidth = 760;
    private const int DetailsHeight = 300;
    // Labels inset their text by a few px (GDI text padding); text boxes don't, so indent the
    // boxes by the same amount to line their left edge up with the help/label text above them.
    private const int TextInset = 3;

    private readonly AppConfig _config;
    /// <summary>Told the game folder and the emulator save folders the game's unlocks will appear in.</summary>
    private readonly Action<string, IReadOnlyList<string>> _onGameConfigured;
    private readonly SteamAppNames _appNames;
    private readonly ToolTip _toolTip = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly System.Windows.Forms.Timer _spinner = new() { Interval = 90 };

#pragma warning disable CA2213 // WinForms controls: every one sits in the form's Controls tree, and the form disposes them through it
    // Pages
    private readonly Panel _content = new() { Dock = DockStyle.Fill };
    private Panel _folderPage = null!;
    private Panel _appIdPage = null!;
    private Panel _apiKeyPage = null!;
    private Panel _firecrawlPage = null!;
    private Panel _readyPage = null!;
    private Panel _progressPage = null!;

    // Inputs
    private readonly TextBox _gameDirBox = new() { BorderStyle = BorderStyle.FixedSingle };
    private readonly TextBox _appIdBox = new() { BorderStyle = BorderStyle.FixedSingle };
    private readonly TextBox _apiKeyBox = new() { BorderStyle = BorderStyle.FixedSingle, UseSystemPasswordChar = true };
    private readonly TextBox _firecrawlBox = new() { BorderStyle = BorderStyle.FixedSingle };
    private readonly TextBox _gbePathBox = new() { BorderStyle = BorderStyle.FixedSingle };
    private readonly CheckBox _backupCheck = new() { Text = "Back up the original Steam library (recommended)", Checked = true, AutoSize = true, Margin = new Padding(0, 14, 0, 6) };
    private readonly CheckBox _downloadGbeCheck = new() { Text = "Download the latest GBE release", Checked = true, AutoSize = true, Margin = new Padding(0, 4, 0, 2) };
    private readonly CheckBox _replaceEmulatorCheck = new() { Text = "Replace it with GBE", AutoSize = true, Margin = new Padding(0, 10, 0, 6) };
    private readonly List<Control> _gbeControls = new();
    private readonly List<Control> _emulatorControls = new();
    private Label _keepEmulatorHelp = null!;
    private Label _backupHelp = null!;
    private Label _downloadHelp = null!;
    private readonly Label _advancedToggle = new() { AutoSize = true, Cursor = Cursors.Hand, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Margin = new Padding(0, 10, 0, 2) };
    private readonly List<Control> _advancedControls = new();
    private bool _advancedExpanded;
    private bool _placed;
    private TableLayoutPanel _root = null!;
    private bool _detailsExpanded;
    /// <summary>The dialog's client width with the details hidden, captured each time they open.</summary>
    private int _collapsedWidth;
    /// <summary>The client width and log height with the details open, kept so a drag outlives a collapse and a refit;
    /// 0 until they first open.</summary>
    private int _expandedWidth;
    private int _detailsHeight;
    /// <summary>Set while <see cref="FitToActivePage"/> resizes the dialog, so only the user's own resize is recorded.</summary>
    private bool _fitting;
    private readonly Label _folderStatus = new() { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(HelpWidth, 0), Margin = new Padding(0, 6, 0, 0) };
    private readonly LinkLabel _steamDbLink = new() { AutoSize = true };
    private readonly Label _appIdNote = new() { AutoSize = true, MaximumSize = new Size(HelpWidth, 0), ForeColor = Color.DarkGoldenrod, Margin = new Padding(0, 0, 0, 6) };
    private readonly Label _summaryGameKey = new() { Text = "Game folder:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 12, 2) };
    private readonly Label _summaryAppIdKey = new() { Text = "Steam AppID:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 12, 0) };
    private readonly Label _summaryGameValue = new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 0, 2) };
    private readonly Label _summaryAppIdValue = new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
    private readonly Label _summaryEmulatorKey = new() { Text = "Steam emulator:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 2, 12, 0) };
    private readonly Label _summaryEmulatorValue = new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 2, 0, 0) };
    private Label _hiddenInfo = null!;

    // Progress checklist: steps accumulate as rows; the running one spins, finished ones get a check.
    private TableLayoutPanel _stepsTable = null!;
    private static readonly string[] SpinnerFrames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
    private Label? _currentStepIcon;
    private Label? _currentStepLabel;
    private int _spinnerFrame;

    private readonly TextBox _logBox = new()
    {
        Dock = DockStyle.Fill,
        Visible = false,
        Margin = new Padding(3, 10, 3, 0), // the 3 px every page sits in by, as _content's default margin
        AutoSize = false,
        Multiline = true,
        WordWrap = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BackColor = Color.White,
        Font = new Font("Consolas", 9f)
    };

    // Navigation
    private readonly Button _backButton = new() { Text = "Back", AutoSize = true };
    private readonly Button _nextButton = new() { Text = "Next", AutoSize = true };
    private readonly Button _closeButton = new() { Text = "Cancel", AutoSize = true };
    private readonly Button _detailsToggle = new() { AutoSize = true, Visible = false };
    /// <summary>A finished run's outcome, beside the buttons that act on it.</summary>
    private readonly Label _resultLabel = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = new Padding(0, 8, 8, 0) };

    private readonly List<Page> _steps = new();
    private int _stepIndex;
    private readonly List<Button> _browseButtons = new();
    private readonly List<Label> _wrapLabels = new();
    private Panel? _activePanel;
    private FlowLayoutPanel _buttonBar = null!;
#pragma warning restore CA2213

    // Collected state
    private string _gameDir = "";
    private string _appId = "";
    private bool _appIdGuessed;
    /// <summary>The CODEX/RUNE emulator the game runs, or null when it runs Steam's own DLL or GBE.</summary>
    private SceneEmulator? _emulator;
    /// <summary>Whether the game already loads a GBE steamclient through ColdClientLoader, so nothing is installed.</summary>
    private bool _keepsGbe;
    private bool _needAppId;
    private bool _needApiKey;
    private bool _force; // overwrite an existing steam_settings (decided right after folder selection)
    private List<SteamSchemaAchievement>? _schema;
    private bool _schemaFetched;
    private int _hiddenCount;
    private CancellationTokenSource? _cts;
    private bool _running;
    /// <summary>Whether the last close request was refused, which a run in progress does.</summary>
    private bool _closeRefused;

    public AddGameForm(AppConfig config, SteamAppNames appNames, Action<string, IReadOnlyList<string>> onGameConfigured)
    {
        _appNames = appNames;
        _config = config;
        _onGameConfigured = onGameConfigured;

        Text = "Add game";
        Icon = AppUtilities.LoadAppIcon(false);
        StartPosition = FormStartPosition.Manual; // placed by FitToActivePage once it knows the first page's size
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(FormWidth, 440);
        Padding = new Padding(12);

        BuildPages();
        BuildChrome();

        // The link follows the AppID box, so a guessed or typed AppID opens its own SteamDB page for checking.
        _appIdBox.TextChanged += (_, _) => UpdateSteamDbLink();
        _steamDbLink.LinkClicked += (_, _) => OpenUrl(TypedAppId() is { } id ? SteamDbScraper.AppPageUrl(id) : "https://steamdb.info/");
        UpdateSteamDbLink();

        _gbePathBox.Text = GbeBinaryManager.CacheRoot;
        _gbePathBox.TextChanged += (_, _) => UpdateGbeDownloadState();
        UpdateGbeDownloadState();

        _advancedToggle.Click += (_, _) => SetAdvancedExpanded(!_advancedExpanded);
        _replaceEmulatorCheck.CheckedChanged += (_, _) => UpdateEmulatorControls();
        SetAdvancedExpanded(false);

        _spinner.Tick += (_, _) =>
        {
            if (_currentStepIcon == null)
                return;
            _spinnerFrame = (_spinnerFrame + 1) % SpinnerFrames.Length;
            _currentStepIcon.Text = SpinnerFrames[_spinnerFrame];
        };

        _steps.Add(Page.Folder);
        _stepIndex = 0;
        ShowStep();
    }

    /// <summary>Shows or hides the Advanced (GBE release folder) controls and re-fits the page.</summary>
    private void SetAdvancedExpanded(bool expanded)
    {
        _advancedExpanded = expanded;
        foreach (var c in _advancedControls)
            c.Visible = expanded && InstallsGbe;
        _advancedToggle.Text = (expanded ? "▾" : "▸") + " Advanced options";
        if (expanded)
            UpdateGbeDownloadState();
        FitToActivePage();
    }

    /// <summary>The "download latest" option is forced on when the folder has no GBE release to reuse.</summary>
    private void UpdateGbeDownloadState()
    {
        var hasExisting = GbeBinaryManager.LocateBinaries(_gbePathBox.Text.Trim(), "") != null;
        if (hasExisting)
        {
            _downloadGbeCheck.Enabled = true;
        }
        else
        {
            _downloadGbeCheck.Checked = true;
            _downloadGbeCheck.Enabled = false;
        }
    }

    /// <summary>Whether this run installs GBE: always, unless GBE is already in place or the game runs CODEX/RUNE and the user keeps it.</summary>
    private bool InstallsGbe => !_keepsGbe && (_emulator == null || _replaceEmulatorCheck.Checked);

    /// <summary>Shows the emulator rows only for a CODEX/RUNE game, and the GBE options only when GBE is installed.</summary>
    private void UpdateEmulatorControls()
    {
        foreach (var c in _emulatorControls)
            c.Visible = _emulator != null;
        foreach (var c in _gbeControls)
            c.Visible = InstallsGbe;

        // One text for both choices: it sits above the checkbox, so a wording that changed with the tick would
        // change its height and move the checkbox out from under the pointer.
        _keepEmulatorHelp.Text = $"This game runs the {_emulator?.Name} Steam emulator, which records each unlock in a "
            + "file of its own. By default the overlay reads that file, adds only the achievement list and icons, and "
            + "leaves the game's Steam library as it is. Ticking the box below replaces the emulator with GBE instead.";

        if (_emulator == null)
        {
            _backupCheck.Text = "Back up the original Steam library (recommended)";
            _backupHelp.Text = "To track achievements, the overlay replaces the game's Steam library (steam_api64.dll) with GBE's "
                + "drop-in version. GBE emulates the Steam API locally and records each unlock to a file this app watches, "
                + "so tracking works without Steam running.\r\n\r\n"
                + "With this on, the original library is first copied to steam_api64.dll.original — keep it so you can "
                + "restore the game's normal Steam integration later (it's also the file the tool reads to generate the "
                + "interface list GBE needs).";
        }
        else
        {
            _backupCheck.Text = "Back up the emulator's library (recommended)";
            _backupHelp.Text = $"The overlay replaces the {_emulator.Name} emulator's library (steam_api64.dll) with GBE's "
                + "drop-in version, which records each unlock to a file this app watches. The interface list GBE needs is "
                + $"taken from the emulator's {SceneEmulatorDetector.EmuIni}.\r\n\r\n"
                + "With this on, the emulator's library is first copied to steam_api64.dll.original — keep it so you can "
                + "put the emulator back later.";
        }

        SetAdvancedExpanded(_advancedExpanded);
    }

    // --- Layout ---

    private void BuildChrome()
    {
        var root = _root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(_content, 0, 0);

        _buttonBar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        _closeButton.Click += OnCloseClick;
        _nextButton.Click += OnNextClick;
        _backButton.Click += (_, _) => { if (_stepIndex > 0) { _stepIndex--; ShowStep(); } };
        _detailsToggle.Click += (_, _) => SetDetailsExpanded(!_detailsExpanded);
        SetDetailsToggleText();
        _buttonBar.Controls.AddRange(new Control[] { _nextButton, _closeButton, _backButton, _detailsToggle });
        var bottomRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, AutoSize = true, Margin = Padding.Empty };
        bottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottomRow.Controls.Add(_resultLabel, 0, 0);
        bottomRow.Controls.Add(_buttonBar, 1, 0);
        root.Controls.Add(bottomRow, 0, 1);
        root.Controls.Add(_logBox, 0, 2);

        Controls.Add(root);
        AcceptButton = _nextButton;
    }

    private void BuildPages()
    {
        _wrapLabels.Add(_folderStatus);
        _wrapLabels.Add(_appIdNote);

        _folderPage = MakePage(
            "Select game folder",
            "The tool searches the folder you pick — including deeply nested Unreal Engine paths — for the game's Steam "
            + "library file (steam_api64.dll or steam_api.dll). That file's folder is where the achievement configuration "
            + "will be written.",
            null,
            g =>
            {
                var folderRow = DialogControls.MakeInputRow(_gameDirBox, MakeBrowseButton(_gameDirBox));
                folderRow.Margin = new Padding(TextInset, 28, TextInset, 2); // gap before the box; inset to align with text
                g.Controls.Add(folderRow);
                _folderStatus.Margin = new Padding(0, 6, 0, 0);
                g.Controls.Add(_folderStatus);
            });

        _appIdPage = MakePage(
            "Steam AppID",
            "This game's AppID couldn't be detected automatically. Every Steam game has a unique numeric ID — for example, "
            + "620 is Portal 2. You can read it from the game's Steam store URL (the number in "
            + "store.steampowered.com/app/<id>/) or from SteamDB.",
            null,
            g =>
            {
                _appIdNote.Margin = new Padding(0, 0, 0, 4);
                g.Controls.Add(_appIdNote);
                _steamDbLink.Margin = new Padding(0, 0, 0, 6);
                g.Controls.Add(_steamDbLink);
                _appIdBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                _appIdBox.Margin = new Padding(TextInset, 2, TextInset, 2);
                g.Controls.Add(_appIdBox);
            });

        _apiKeyPage = MakePage(
            "Steam Web API key",
            "A free personal key tied to your Steam account. The tool uses it to read the game's official achievement "
            + "schema — names, descriptions, and icon URLs — from Steam's Web API (api.steampowered.com). It's sent only to "
            + "Steam and saved in this app's config, so you enter it once and reuse it for every game. You don't need to own "
            + "the game on this account — any valid key can read any game's public achievement list.\r\n\r\n"
            + "Note: the key is stored unencrypted in config.json. If that's a concern, you can revoke it on the same page "
            + "right after the game is added — it's only needed while adding a game (you'll just enter a fresh key next time "
            + "you add one).",
            ("Get a Steam Web API key", "https://steamcommunity.com/dev/apikey"),
            g =>
            {
                _apiKeyBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                _apiKeyBox.Margin = new Padding(TextInset, 28, TextInset, 2); // match folder page spacing; inset to align with text
                g.Controls.Add(_apiKeyBox);
            });

        _firecrawlPage = MakePage(
            "Hidden achievements",
            null, // the intro text is built at runtime (it includes the achievement counts)
            null,
            g =>
            {
                // These paragraphs are separate controls (for the inline link), so give them a
                // bottom margin equal to a blank line to match the \r\n\r\n paragraph spacing elsewhere.
                var paraGap = Font.Height;

                _hiddenInfo = MakeHelp(""); // text set in OnEnterPage once the counts are known
                _hiddenInfo.Margin = new Padding(0, 0, 0, paraGap);
                g.Controls.Add(_hiddenInfo);

                var linkPara = MakeInlineLinkHelp(
                    "The real text can be fetched from SteamDB, which is behind Cloudflare, so the tool reads it through "
                    + "Firecrawl — a hosted scraper. Create a free account at firecrawl.dev, copy your API key, and paste "
                    + "it below.",
                    "firecrawl.dev", "https://www.firecrawl.dev/");
                linkPara.Margin = new Padding(0, 0, 0, paraGap);
                g.Controls.Add(linkPara);

                g.Controls.Add(MakeHelp("Leave it blank to skip — hidden achievements will just show a placeholder description."));
                _firecrawlBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                _firecrawlBox.Margin = new Padding(TextInset, 28, TextInset, 2); // match the spacing before the box on other pages
                g.Controls.Add(_firecrawlBox);
            });

        _readyPage = MakePage(
            "Ready to add",
            "Review the options below, then add the game. The overlay starts tracking it right away — no restart.",
            null,
            g =>
            {
                g.Controls.Add(BuildSummary());

                _keepEmulatorHelp = MakeHelp("");
                var replaceHelp = MakeHelp(
                    "Only if this game's achievements never appear: some games record no unlocks under this emulator, "
                    + "and GBE does.");
                g.Controls.Add(_keepEmulatorHelp);
                g.Controls.Add(_replaceEmulatorCheck);
                g.Controls.Add(replaceHelp);
                _emulatorControls.AddRange(new Control[] { _keepEmulatorHelp, _replaceEmulatorCheck, replaceHelp });

                _backupHelp = MakeHelp(""); // text set in UpdateEmulatorControls, which knows what is being replaced
                g.Controls.Add(_backupCheck);
                g.Controls.Add(_backupHelp);
                g.Controls.Add(_advancedToggle);
                _gbeControls.AddRange(new Control[] { _backupCheck, _backupHelp, _advancedToggle });

                var gbeLabel = new Label { Text = "GBE release folder", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 14, 0, 6) };
                var gbeRow = DialogControls.MakeInputRow(_gbePathBox, MakeBrowseButton(_gbePathBox));
                gbeRow.Margin = new Padding(TextInset, 6, TextInset, 12); // align the box edge with the text; extra space above/below
                var gbeHelp = MakeHelp(
                    "GBE provides the Steam emulator that's copied into the game (and the tool that lists its Steam "
                    + "interfaces). The latest release is downloaded here and reused for future games — you don't need it to "
                    + "play, but keeping it avoids re-downloading.\r\n\r\n"
                    + "Uncheck this to reuse a GBE release already in the folder (for example if Windows Defender blocks the "
                    + "download); that's only possible when the folder already contains one.");
                g.Controls.Add(gbeLabel);
                g.Controls.Add(gbeRow);
                g.Controls.Add(_downloadGbeCheck);
                g.Controls.Add(gbeHelp);
                _advancedControls.AddRange(new Control[] { gbeLabel, gbeRow, _downloadGbeCheck, gbeHelp });
            });

        _progressPage = MakePage(
            "Adding game…",
            null,
            null,
            g =>
            {
                _downloadHelp = MakeHelp("This can take a minute the first time, while the GBE emulator is downloaded.");
                g.Controls.Add(_downloadHelp);
                _stepsTable = new TableLayoutPanel { ColumnCount = 2, Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 4, 0, 0) };
                _stepsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
                _stepsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                g.Controls.Add(_stepsTable);
            });

        foreach (var page in new[] { _folderPage, _appIdPage, _apiKeyPage, _firecrawlPage, _readyPage, _progressPage })
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            _content.Controls.Add(page);
        }
    }

    /// <summary>Builds a page: bold header, wrapped help, optional link, then body controls added
    /// directly to one grid (so wrapping labels measure reliably). The form resizes to fit it.</summary>
    private Panel MakePage(string header, string? help, (string text, string url)? link, Action<TableLayoutPanel> addBody)
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        grid.Controls.Add(MakeHeader(header));
        if (!string.IsNullOrEmpty(help))
            grid.Controls.Add(MakeHelp(help));
        if (link != null)
            grid.Controls.Add(MakeLink(link.Value.text, link.Value.url));

        addBody(grid);

        var page = new Panel { Tag = grid };
        page.Controls.Add(grid);
        return page;
    }

    /// <summary>A bold page header label.</summary>
    private Label MakeHeader(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font(Font.FontFamily, Font.Size + 2f, FontStyle.Bold),
        Margin = new Padding(0, 0, 0, 14)
    };

    /// <summary>A two-column summary whose values (folder, AppID) align on the left edge,
    /// with padding above and below.</summary>
    private TableLayoutPanel BuildSummary()
    {
        var summary = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 10, 0, 14) };
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        summary.Controls.Add(_summaryGameKey, 0, 0);
        summary.Controls.Add(_summaryGameValue, 1, 0);
        summary.Controls.Add(_summaryAppIdKey, 0, 1);
        summary.Controls.Add(_summaryAppIdValue, 1, 1);
        summary.Controls.Add(_summaryEmulatorKey, 0, 2);
        summary.Controls.Add(_summaryEmulatorValue, 1, 2);
        _emulatorControls.AddRange(new Control[] { _summaryEmulatorKey, _summaryEmulatorValue });
        return summary;
    }

    private Button MakeBrowseButton(TextBox target)
    {
        var button = DialogControls.MakeBrowseButton(_toolTip, "Browse for folder", () => PickFolder(target));
        _browseButtons.Add(button);
        return button;
    }

    /// <summary>Gray wrapping help-text label. Its wrap width is set in <see cref="FitToActivePage"/>.</summary>
    private Label MakeHelp(string text)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(HelpWidth, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 4)
        };
        _wrapLabels.Add(label);
        return label;
    }

    /// <summary>Gray wrapping help-text label like <see cref="MakeHelp"/>, but with one substring
    /// rendered as a clickable link to <paramref name="url"/>.</summary>
    private LinkLabel MakeInlineLinkHelp(string text, string linkText, string url)
    {
        var label = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(HelpWidth, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 4)
        };
        var idx = text.IndexOf(linkText, StringComparison.Ordinal);
        if (idx >= 0)
            label.LinkArea = new LinkArea(idx, linkText.Length);
        label.LinkClicked += (_, _) => OpenUrl(url);
        _wrapLabels.Add(label);
        return label;
    }

    private static LinkLabel MakeLink(string text, string url)
    {
        var link = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
        link.LinkClicked += (_, _) => OpenUrl(url);
        return link;
    }

    // --- Navigation ---

    private void ShowStep()
    {
        _resultLabel.Text = "";
        var page = _steps[_stepIndex];
        ShowPage(page);
        _backButton.Visible = _stepIndex > 0;
        _nextButton.Visible = true;
        _nextButton.Text = page == Page.Ready ? "Add game" : "Next";
        _closeButton.Text = "Cancel";
        _detailsToggle.Visible = false;
        OnEnterPage(page);
        FitToActivePage();
    }

    private void ShowPage(Page page)
    {
        _activePanel = page switch
        {
            Page.Folder => _folderPage,
            Page.AppId => _appIdPage,
            Page.ApiKey => _apiKeyPage,
            Page.Firecrawl => _firecrawlPage,
            Page.Ready => _readyPage,
            _ => _progressPage
        };
        foreach (Control c in _content.Controls)
            c.Visible = c == _activePanel;
        _activePanel.BringToFront();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        // Per-monitor DPI changed (moved to another monitor or scale changed) — re-fit the page.
        BeginInvoke(FitToActivePage);
    }

    /// <summary>The height of the content row that holds exactly the page <paramref name="grid"/>, which is what the row
    /// is pinned to while the details log is open.</summary>
    private int ContentRowHeight(Control grid) => grid.Height + _content.Margin.Vertical;

    /// <summary>Wraps help text to the current width and resizes the dialog to the active page's height, plus the
    /// details log below the buttons when it is open.</summary>
    private void FitToActivePage()
    {
        if (!IsHandleCreated || _activePanel?.Tag is not Control grid)
            return;

        _fitting = true;
        try
        {
            var details = _activePanel == _progressPage && _detailsExpanded;
            ApplyDetailsMode(details);
            WrapHelpLabels();

            // Lay out at the current width, then size the dialog so the page's grid fits exactly. (The grid's
            // PreferredSize under-measures stacked wrapped labels, but its realized height is correct.) The button
            // row is measured from the realized layout too: a hand-summed constant under-counted it by a few px,
            // clipping the last line of a label that had just wrapped.
            PerformLayout();
            var contentRow = ContentRowHeight(grid);
            var detailsRow = details ? _detailsHeight + _logBox.Margin.Vertical : 0;
            // With the log open the page keeps exactly its own height, so the log takes whatever the user adds.
            _root.RowStyles[0] = details ? new RowStyle(SizeType.Absolute, contentRow) : new RowStyle(SizeType.Percent, 100);
            _root.RowStyles[2] = details ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize);
            if (WindowState != FormWindowState.Normal)
                return;

            var desired = Padding.Vertical + contentRow + _root.GetRowHeights()[1] + detailsRow;
            var chrome = Height - ClientSize.Height;
            var maxClient = Screen.FromControl(this).WorkingArea.Height - chrome - 16;

            // Normally the dialog is sized exactly to its content (no scrollbar). If the content is taller than the
            // screen (small resolution / high zoom), clamp to the screen and let the page scroll so nothing is
            // unreachable; an open log gives up its height first.
            var clamped = desired > maxClient;
            _activePanel.AutoScroll = clamped && !details;
            ClientSize = new Size(ClientSize.Width, clamped ? maxClient : desired);

            // The top-left corner stays where it first lands, so a page or an option that changes the size grows the
            // dialog downward and rightward instead of moving everything on screen. It first lands a little above
            // the middle to leave room for the taller pages that follow, and moves only when an edge would leave the
            // screen. First placement goes on the monitor under the pointer, as a centred dialog would; after that,
            // the one the dialog is on.
            var area = (_placed ? Screen.FromControl(this) : Screen.FromPoint(Cursor.Position)).WorkingArea;
            if (!_placed)
            {
                Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + Math.Max(0, (area.Height - Height) / 3));
                _placed = true;
                return;
            }
            if (Bottom > area.Bottom)
                Top = Math.Max(area.Top, area.Bottom - Height);
            if (Right > area.Right)
                Left = Math.Max(area.Left, area.Right - Width);
        }
        finally
        {
            _fitting = false;
        }
    }

    /// <summary>Shows or hides the log and switches between the fixed dialog and the wider, resizable one.</summary>
    private void ApplyDetailsMode(bool details)
    {
        if (_logBox.Visible == details)
            return;

        if (details)
        {
            _collapsedWidth = ClientSize.Width;
            if (_expandedWidth == 0)
            {
                // Scaled from the collapsed width rather than from DPI, so the two keep the proportion they were
                // designed at whatever scaling the dialog was laid out under.
                _expandedWidth = _collapsedWidth * DetailsWidth / FormWidth;
                _detailsHeight = _collapsedWidth * DetailsHeight / FormWidth;
            }
        }
        else if (WindowState != FormWindowState.Normal)
        {
            WindowState = FormWindowState.Normal;
        }

        MinimumSize = Size.Empty;
        FormBorderStyle = details ? FormBorderStyle.Sizable : FormBorderStyle.FixedDialog;
        MaximizeBox = details;
        _logBox.Visible = details;
        ClientSize = new Size(details ? _expandedWidth : _collapsedWidth, ClientSize.Height);
        if (details)
            MinimumSize = new Size(Width - ClientSize.Width + _collapsedWidth, 0);
    }

    private void SetDetailsExpanded(bool expanded)
    {
        _detailsExpanded = expanded;
        SetDetailsToggleText();
        FitToActivePage();
        // Lines appended while the log was hidden never scrolled it, and the newest line is the one that
        // explains the result. After the resize, so the box scrolls at its final height.
        if (expanded)
            BeginInvoke(() => { _logBox.SelectionStart = _logBox.TextLength; _logBox.ScrollToCaret(); });
    }

    private void SetDetailsToggleText() => _detailsToggle.Text = _detailsExpanded ? "Hide details \u25B4" : "Show details \u25BE";

    private void WrapHelpLabels()
    {
        var wrap = Math.Max(200, ClientSize.Width - Padding.Horizontal);
        foreach (var label in _wrapLabels)
            label.MaximumSize = new Size(wrap, 0);
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        // A resize by the user, which only the open log allows: re-wrap the page at the new width, hold it at its own
        // height, and remember the size so the next refit keeps it.
        if (_fitting || !_logBox.Visible || _activePanel?.Tag is not Control grid)
            return;
        WrapHelpLabels();
        PerformLayout();
        _root.RowStyles[0] = new RowStyle(SizeType.Absolute, ContentRowHeight(grid));
        PerformLayout();
        if (WindowState != FormWindowState.Normal)
            return;
        _expandedWidth = ClientSize.Width;
        _detailsHeight = _logBox.Height;
    }

    private void OnEnterPage(Page page)
    {
        switch (page)
        {
            case Page.Folder:
                _gameDirBox.Focus();
                break;
            case Page.AppId:
                _appIdBox.Text = _appId;
                _appIdNote.Visible = _appIdGuessed;
                if (_appIdGuessed)
                    _appIdNote.Text = $"Guessed {_appId} from a Steam store search — please verify it's the right game.";
                _appIdBox.Focus();
                break;
            case Page.ApiKey:
                if (string.IsNullOrEmpty(_apiKeyBox.Text))
                    _apiKeyBox.Text = _config.SteamWebApiKey ?? "";
                _apiKeyBox.Focus();
                break;
            case Page.Firecrawl:
                _hiddenInfo.Text =
                    $"Out of {_schema?.Count ?? 0} achievements in this game, {_hiddenCount} are hidden — Steam leaves their "
                    + "descriptions blank.";
                if (string.IsNullOrEmpty(_firecrawlBox.Text))
                    _firecrawlBox.Text = _config.FirecrawlApiKey ?? "";
                _firecrawlBox.Focus();
                break;
            case Page.Ready:
                _summaryGameValue.Text = _gameDir;
                _summaryAppIdValue.Text = _appId;
                _summaryEmulatorValue.Text = _emulator?.Name ?? "";
                UpdateEmulatorControls();
                UpdateGbeDownloadState();
                break;
        }
    }

    private async void OnNextClick(object? sender, EventArgs e)
    {
        switch (_steps[_stepIndex])
        {
            case Page.Folder:
                await DetectAndAdvanceAsync();
                break;

            case Page.AppId:
                if (TypedAppId() is not { } newAppId)
                {
                    MessageBox.Show(this, "Enter the game's Steam AppID, a number such as 620.", "Add game", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (newAppId != _appId)
                {
                    _appId = newAppId;
                    InvalidateSchema(); // AppID changed — re-fetch on the way to Ready
                }
                await AdvanceOrFetchAsync();
                break;

            case Page.ApiKey:
                if (string.IsNullOrWhiteSpace(_apiKeyBox.Text))
                {
                    MessageBox.Show(this, "A Steam Web API key is required.\nGet one at https://steamcommunity.com/dev/apikey",
                        "Add game", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (_apiKeyBox.Text.Trim() != (_config.SteamWebApiKey ?? ""))
                {
                    _config.UpdateConfigValue(nameof(SettingsData.SteamWebApiKey), _apiKeyBox.Text.Trim());
                    InvalidateSchema(); // API key changed — re-fetch
                }
                await AdvanceOrFetchAsync();
                break;

            case Page.Firecrawl:
                var fckey = _firecrawlBox.Text.Trim();
                if (fckey.Length > 0 && fckey != (_config.FirecrawlApiKey ?? ""))
                    _config.UpdateConfigValue(nameof(SettingsData.FirecrawlApiKey), fckey);
                _stepIndex++;
                ShowStep();
                break;

            case Page.Ready:
                await StartRunAsync();
                break;
        }
    }

    /// <summary>From the last input page, fetch the achievement schema (to validate inputs and decide
    /// whether a hidden-achievements step is needed) before continuing; otherwise just go to the next input.</summary>
    private async Task AdvanceOrFetchAsync()
    {
        if (_steps[_stepIndex + 1] == Page.Ready)
        {
            await ProceedAfterInputsAsync();
        }
        else
        {
            _stepIndex++;
            ShowStep();
        }
    }

    /// <summary>Discards the fetched schema (and any inserted hidden-achievements step) so it's
    /// re-fetched after the AppID or API key changes.</summary>
    private void InvalidateSchema()
    {
        _schemaFetched = false;
        _schema = null;
        _hiddenCount = 0;
        _steps.Remove(Page.Firecrawl);
    }

    private async Task ProceedAfterInputsAsync()
    {
        if (!_schemaFetched)
        {
            SetNav(false);
            UseWaitCursor = true;
            try
            {
                _schema = await SteamWebApi.GetAchievementSchemaAsync(_config.SteamWebApiKey ?? "", _appId, _http);
                _hiddenCount = _schema.Count(a => a.Hidden != 0);
                _schemaFetched = true;
            }
            catch (HttpRequestException ex)
            {
                var proceed = MessageBox.Show(this,
                    $"Couldn't fetch achievements for AppID {_appId}:\r\n{ex.Message}\r\n\r\n"
                    + "This usually means a wrong AppID or Steam Web API key. Continue anyway?",
                    "Add game", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (proceed != DialogResult.Yes)
                {
                    SetNav(true);
                    UseWaitCursor = false;
                    return;
                }
                _schema = null;
                _hiddenCount = 0;
                _schemaFetched = true;
            }
            finally
            {
                SetNav(true);
                UseWaitCursor = false;
            }
        }

        // Only ask for a Firecrawl key when there are hidden achievements and none is saved yet.
        if (_hiddenCount > 0 && string.IsNullOrWhiteSpace(_config.FirecrawlApiKey) && !_steps.Contains(Page.Firecrawl))
            _steps.Insert(_steps.IndexOf(Page.Ready), Page.Firecrawl);

        _stepIndex++;
        ShowStep();
    }

    private async Task DetectAndAdvanceAsync()
    {
        var gameDir = _gameDirBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
        {
            _folderStatus.ForeColor = Color.Firebrick;
            _folderStatus.Text = "Choose a valid game folder.";
            FitToActivePage();
            return;
        }

        SetNav(false);
        _folderStatus.ForeColor = SystemColors.GrayText;
        _folderStatus.Text = "Detecting…";
        try
        {
            var (target, refusal) = await Task.Run(() => InstallTarget.Choose(gameDir));
            if (target == null)
            {
                _folderStatus.ForeColor = Color.Firebrick;
                _folderStatus.Text = refusal + " Pick the game's install folder. This wizard configures Steam games only — other emulators are tracked automatically once they write to the GSE Saves folder.";
                FitToActivePage();
                return;
            }

            _gameDir = gameDir;
            // Here, before any run: registering a game can add a root planned for it to gamesPaths, and
            // only an entry listed before then may be remembered.
            if (GamesPathPlanner.GamesRootOf(_config.GamesPaths, gameDir) is { } gamesRoot)
                RememberGamesRoot(gamesRoot);
            _keepsGbe = target.Action == InstallAction.KeepGbe;
            _emulator = _keepsGbe ? null : await Task.Run(() => SceneEmulatorDetector.Detect(target.Dll.Path));
            _replaceEmulatorCheck.Checked = false;
            // A kept CODEX/RUNE emulator installs nothing, so only a game with none can be refused here.
            if (_emulator == null && target.InstallRefusal is { } cannotInstall)
            {
                _folderStatus.ForeColor = Color.Firebrick;
                _folderStatus.Text = cannotInstall;
                FitToActivePage();
                return;
            }

            // If a config already exists at the target, confirm overwrite up front (not at the end).
            var existing = target.Dll.SettingsDir;
            _force = false;
            if (Directory.Exists(existing))
            {
                var answer = MessageBox.Show(this,
                    $"A Steam emulator configuration already exists here:\r\n\r\n{existing}\r\n\r\nOverwrite it?",
                    "Configuration already exists", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes)
                {
                    _folderStatus.Text = "";
                    return; // stay on the folder page
                }
                _force = true;
            }

            // The emulator reads only the steam_emu.ini beside it, so that one names the game; the generic
            // scan takes the first match anywhere below, which can be another copy.
            var localAppId = _emulator?.AppId
                ?? await Task.Run(() => AppIdResolver.FromAppIdTxt(gameDir) ?? AppIdResolver.FromIniFiles(gameDir));
            string? guessed = null;
            if (localAppId == null && AppIdResolver.StoreSearchName(gameDir) is { } name)
            {
                _folderStatus.Text = "Searching the Steam store…";
                guessed = await AppIdResolver.FromStoreSearchAsync(name, _http);
            }

            _appId = localAppId ?? guessed ?? "";
            _appIdGuessed = localAppId == null && guessed != null;
            _needAppId = localAppId == null;
            _needApiKey = string.IsNullOrWhiteSpace(_config.SteamWebApiKey);

            BuildSteps();
            InvalidateSchema(); // re-detection may change the AppID
            _folderStatus.Text = "";
            if (_steps[1] == Page.Ready)
            {
                await ProceedAfterInputsAsync(); // AppID + key already known — fetch, then ready/cookie
            }
            else
            {
                _stepIndex = 1;
                ShowStep();
            }
        }
#pragma warning disable CA1031 // UI boundary: shows the failure on the folder page's status line
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _folderStatus.ForeColor = Color.Firebrick;
            _folderStatus.Text = ex.Message;
            FitToActivePage();
        }
        finally
        {
            SetNav(true);
        }
    }

    private void BuildSteps()
    {
        _steps.Clear();
        _steps.Add(Page.Folder);
        if (_needAppId)
            _steps.Add(Page.AppId);
        if (_needApiKey)
            _steps.Add(Page.ApiKey);
        _steps.Add(Page.Ready);
    }

    private void SetNav(bool enabled)
    {
        _backButton.Enabled = enabled;
        _nextButton.Enabled = enabled;
    }

    // --- Run ---

    private async Task StartRunAsync()
    {
        _resultLabel.Text = "";
        // The overwrite decision was made right after folder selection (see DetectAndAdvanceAsync).
        var force = _force;

        // The Firecrawl key was captured on the Hidden achievements step (if shown) and saved to config there.
        var firecrawlKey = _config.FirecrawlApiKey;

        var request = new ConfigRequest
        {
            GameDir = _gameDir,
            AppId = _appId,
            ApiKey = _config.SteamWebApiKey ?? "",
            FirecrawlApiKey = string.IsNullOrWhiteSpace(firecrawlKey) ? null : firecrawlKey,
            GbeFolder = string.IsNullOrWhiteSpace(_gbePathBox.Text) ? GbeBinaryManager.CacheRoot : _gbePathBox.Text.Trim(),
            DownloadLatest = _downloadGbeCheck.Checked,
            Backup = _backupCheck.Checked,
            KeepEmulator = !InstallsGbe,
            Force = force
        };

        _logBox.Clear();
        ResetSteps();
        _downloadHelp.Visible = !request.KeepEmulator;
        ShowPage(Page.Progress);
        _backButton.Visible = false;
        _nextButton.Visible = false;
        _detailsToggle.Visible = true;
        FitToActivePage();
        _running = true;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        UseWaitCursor = true;

        ConfigStatus status;
        try
        {
            status = await Task.Run(() => new GbeConfigGenerator(request, this, _http, _schema).RunAsync(_cts.Token));
        }
        catch (OperationCanceledException)
        {
            Report(ConfigLogLevel.Warning, "Cancelled.");
            SetDetailsExpanded(true);
            FinishRun();
            return;
        }
#pragma warning disable CA1031 // UI boundary: shows the failure as an error line in the progress log
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Report(ConfigLogLevel.Error, ex.Message);
            SetDetailsExpanded(true);
            FinishRun();
            return;
        }

        if (status is ConfigStatus.Success or ConfigStatus.PartialSuccess)
        {
            // Before registering, so the "tracking configured" popup the registration can raise already
            // names the game rather than its folder.
            await _appNames.FetchAsync([_appId]);
            try
            {
                _onGameConfigured(_gameDir, _emulator != null && request.KeepEmulator ? _emulator.SavesFolders : []);
            }
#pragma warning disable CA1031 // Host-callback boundary: shows the failure as an error line in the progress log
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Report(ConfigLogLevel.Error, $"Game configured, but updating the overlay failed: {ex.Message}");
                // The files are written but the game may not be tracked yet, so the result must not read as clean.
                status = ConfigStatus.PartialSuccess;
            }
        }

        var success = status is ConfigStatus.Success or ConfigStatus.PartialSuccess;
        _resultLabel.ForeColor = success ? Color.ForestGreen : Color.Firebrick;
        _resultLabel.Text = status switch
        {
            ConfigStatus.Success => "✓ Game added and tracked",
            ConfigStatus.PartialSuccess => "✓ Added with warnings",
            ConfigStatus.UserError => "✗ Could not add the game",
            ConfigStatus.DefenderBlocked => "✗ Blocked by Windows Defender",
            _ => "✗ Failed"
        };
        Logger.Info($"[add-game] Result: {_resultLabel.Text}");
        // Anything short of a clean run is explained only in the log.
        if (status != ConfigStatus.Success)
            SetDetailsExpanded(true);
        FinishRun(success);

        if (status == ConfigStatus.DefenderBlocked)
            await OfferDefenderExclusionsAsync(request);
    }

    /// <summary>On a Defender block, offer to add exclusions (elevated) for the GBE and game folders.</summary>
    private async Task OfferDefenderExclusionsAsync(ConfigRequest request)
    {
        var paths = new[] { request.GbeFolder, _gameDir };
        var answer = MessageBox.Show(this,
            "Add Windows Defender exclusions for these folders so GBE isn't blocked?\r\n\r\n"
            + string.Join("\r\n", paths) + "\r\n\r\n"
            + "Windows will ask for administrator approval, then adding the game continues automatically.",
            "Add Defender exclusions?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes)
            return;

        UseWaitCursor = true;
        var result = await WindowsDefender.AddExclusionsAsync(paths);
        UseWaitCursor = false;

        switch (result)
        {
            case ExclusionResult.Added:
                Report(ConfigLogLevel.Info, "Defender exclusions added — continuing…");
                await StartRunAsync(); // retry automatically (overwrite decision was made up front)
                break;
            case ExclusionResult.Cancelled:
                Report(ConfigLogLevel.Warning, "Defender exclusion cancelled — administrator approval was declined.");
                break;
            default:
                Report(ConfigLogLevel.Error, "Could not add Defender exclusions automatically. Add them manually in "
                    + "Windows Security, or point the Advanced 'GBE release folder' at a pre-extracted GBE release.");
                break;
        }
    }

    private void FinishRun(bool success = false)
    {
        _running = false;
        UseWaitCursor = false;
        FinishStep(success); // resolve the last checklist step (✓ / ✗) and stop the spinner
        _closeButton.Text = "Close";
        // After a failure, re-show the "Add game" button so the user can retry in place; after success, only Close.
        _nextButton.Visible = !success;
    }

    private void OnCloseClick(object? sender, EventArgs e)
    {
        if (_running)
            _cts?.Cancel();
        else
            Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_running)
        {
            _cts?.Cancel();
            e.Cancel = true;
            _closeRefused = true;
            return;
        }
        base.OnFormClosing(e);
        _closeRefused = e.Cancel;
    }

    /// <summary>
    /// Asks the wizard to close, and says whether it did. A run in progress refuses: the run is
    /// cancelled instead, and the wizard stays open to show it stopping.
    /// </summary>
    public bool TryClose()
    {
        Close();
        return !_closeRefused;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var h = _gameDirBox.Height;
        foreach (var button in _browseButtons)
        {
            button.Height = h;
            button.Width = h + 2;
        }
        FitToActivePage();
    }

    private void RememberGamesRoot(string root)
    {
        try
        {
            _config.UpdateConfigValue(nameof(SettingsData.LastGamesRoot), root);
        }
        catch (ConfigSaveException ex)
        {
            // Only where the next folder picker opens depends on it, so the wizard carries on.
            Logger.Warn($"Could not remember the games folder '{root}': {ex.Message}");
        }
    }

    /// <summary>The AppID box's text when it has the form of an AppID, otherwise null.</summary>
    private string? TypedAppId()
    {
        var text = _appIdBox.Text.Trim();
        return AppIdResolver.IsAppId(text) ? text : null;
    }

    private void UpdateSteamDbLink() => _steamDbLink.Text = TypedAppId() is { } id ? $"Open app {id} on SteamDB" : "Look up a game on SteamDB";

    private void PickFolder(TextBox target)
    {
        var current = target.Text.Trim();
        var openIn = current.Length == 0 && target == _gameDirBox ? GamesPathPlanner.ChooseBrowseStart(_config.GamesPaths, _config.GetCurrent().LastGamesRoot) : null;
        var picked = DialogControls.PickFolder(this, current, openIn);
        if (picked != null)
            target.Text = picked;
    }

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;
        if (!url.Contains("://"))
            url = "https://" + url; // auto-detected links may be scheme-less (e.g. "steamdb.info")
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
#pragma warning disable CA1031 // Link-click boundary: logs the failure at Warn
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Logger.Warn($"Could not open '{url}': {ex.Message}");
        }
    }

    // --- IConfigProgress: marshal progress onto the UI thread ---

    void IConfigProgress.Report(ConfigLogLevel level, string message) => Report(level, message);

    private void Report(ConfigLogLevel level, string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Report(level, message));
            return;
        }

        var prefix = level switch
        {
            ConfigLogLevel.Step => "==> ",
            ConfigLogLevel.Warning => "warning: ",
            ConfigLogLevel.Error => "error: ",
            ConfigLogLevel.Debug => "  ",
            _ => ""
        };
        _logBox.AppendText(prefix + message + Environment.NewLine);
        // At the line's own level, so a reader scanning the log for [WARN] and [ERROR] finds the wizard's too.
        Action<string> log = level switch { ConfigLogLevel.Warning => Logger.Warn, ConfigLogLevel.Error => Logger.Error, _ => Logger.Info };
        log($"[add-game] {level}: {message}");

        // Each step becomes a checklist row: finish the previous one, start spinning the new one.
        if (level == ConfigLogLevel.Step)
            BeginStep(message);
    }

    // --- Progress checklist ---

    private void ResetSteps()
    {
        _spinner.Stop();
        _currentStepIcon = null;
        _stepsTable.SuspendLayout();
        _stepsTable.Controls.Clear();
        _stepsTable.RowStyles.Clear();
        _stepsTable.RowCount = 0;
        _stepsTable.ResumeLayout();
    }

    private void BeginStep(string text)
    {
        FinishStep(true); // the previous step is done once the next one starts

        var row = _stepsTable.RowCount;
        _stepsTable.RowCount = row + 1;
        _stepsTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var rowHeight = Font.Height + 4; // tall enough for descenders at the current DPI
        var icon = new Label
        {
            Text = SpinnerFrames[0],
            Font = new Font("Segoe UI Symbol", 9f),
            AutoSize = false,
            Width = 20,
            Height = rowHeight,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 1, 2, 1)
        };
        var label = new Label { Text = text, AutoSize = false, Height = rowHeight, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = new Padding(0, 1, 0, 1) };
        _stepsTable.Controls.Add(icon, 0, row);
        _stepsTable.Controls.Add(label, 1, row);

        _currentStepIcon = icon;
        _currentStepLabel = label;
        _spinnerFrame = 0;
        _spinner.Start();
        FitToActivePage(); // the checklist grows a row, and the dialog with it
    }

    /// <summary>Marks the running step done (✓) or failed (✗) and stops the spinner.</summary>
    private void FinishStep(bool ok)
    {
        if (_currentStepIcon == null)
            return;
        // "Downloading icons..." reads as still running once it carries a tick.
        _currentStepLabel?.Text = TrimTrailingEllipsis(_currentStepLabel.Text);
        _currentStepIcon.Font = new Font("Segoe UI Symbol", 9f, FontStyle.Bold);
        _currentStepIcon.Text = ok ? "✓" : "✗"; // ✓ / ✗
        _currentStepIcon.ForeColor = ok ? Color.SeaGreen : Color.Firebrick;
        _currentStepIcon = null;
        _currentStepLabel = null;
        _spinner.Stop();
    }

    private static string TrimTrailingEllipsis(string text) =>
        text.EndsWith("...", StringComparison.Ordinal) ? text[..^3] : text.EndsWith('…') ? text[..^1] : text;

    /// <summary>Updates the running step's text in place (e.g. with a percentage).</summary>
    void IConfigProgress.UpdateStep(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ((IConfigProgress)this).UpdateStep(text));
            return;
        }
        _currentStepLabel?.Text = text;
    }

    /// <summary>Marks the current step failed (✗), logs the reason, and lets the run continue.</summary>
    void IConfigProgress.FailStep(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ((IConfigProgress)this).FailStep(message));
            return;
        }
        Report(ConfigLogLevel.Warning, message);
        FinishStep(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
            _http.Dispose();
            _spinner.Dispose();
            _cts?.Dispose();
        }
        base.Dispose(disposing);
    }
}
