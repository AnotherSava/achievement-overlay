using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Windows.Forms;
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
    // Labels inset their text by a few px (GDI text padding); text boxes don't, so indent the
    // boxes by the same amount to line their left edge up with the help/label text above them.
    private const int TextInset = 3;

    private readonly AppConfig _config;
    private readonly Action<string> _onGameConfigured;
    private readonly ToolTip _toolTip = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };

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
    private readonly Label _advancedToggle = new() { AutoSize = true, Cursor = Cursors.Hand, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Margin = new Padding(0, 10, 0, 2) };
    private readonly List<Control> _advancedControls = new();
    private bool _advancedExpanded;
    private readonly Label _folderStatus = new() { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(HelpWidth, 0), Margin = new Padding(0, 6, 0, 0) };
    private readonly Label _appIdNote = new() { AutoSize = true, MaximumSize = new Size(HelpWidth, 0), ForeColor = Color.DarkGoldenrod, Margin = new Padding(0, 0, 0, 6) };
    private readonly Label _summaryGameKey = new() { Text = "Game folder:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 12, 2) };
    private readonly Label _summaryAppIdKey = new() { Text = "Steam AppID:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 12, 0) };
    private readonly Label _summaryGameValue = new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 0, 2) };
    private readonly Label _summaryAppIdValue = new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
    private Label _hiddenInfo = null!;

    // Progress checklist: steps accumulate as rows; the running one spins, finished ones get a check.
    private Panel _stepsHost = null!;
    private TableLayoutPanel _stepsTable = null!;
    private readonly System.Windows.Forms.Timer _spinner = new() { Interval = 90 };
    private static readonly string[] SpinnerFrames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
    private Label? _currentStepIcon;
    private Label? _currentStepLabel;
    private int _spinnerFrame;

    private readonly TextBox _logBox = new()
    {
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
        Height = 130,
        AutoSize = false,
        Multiline = true,
        WordWrap = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BackColor = Color.White,
        Font = new Font(FontFamily.GenericMonospace, 8.25f)
    };

    // Navigation
    private readonly Button _backButton = new() { Text = "Back", AutoSize = true };
    private readonly Button _nextButton = new() { Text = "Next", AutoSize = true };
    private readonly Button _closeButton = new() { Text = "Cancel", AutoSize = true };

    private readonly List<Page> _steps = new();
    private int _stepIndex;
    private readonly List<Button> _browseButtons = new();
    private readonly List<Label> _wrapLabels = new();
    private Panel? _activePanel;
    private FlowLayoutPanel _buttonBar = null!;

    // Collected state
    private string _gameDir = "";
    private string? _dllPrimaryPath;
    private string _appId = "";
    private bool _appIdGuessed;
    private bool _needAppId;
    private bool _needApiKey;
    private bool _force; // overwrite an existing steam_settings (decided right after folder selection)
    private List<SteamSchemaAchievement>? _schema;
    private bool _schemaFetched;
    private int _hiddenCount;
    private CancellationTokenSource? _cts;
    private bool _running;

    public AddGameForm(AppConfig config, Action<string> onGameConfigured)
    {
        _config = config;
        _onGameConfigured = onGameConfigured;

        Text = "Add game";
        Icon = AppUtilities.LoadOrCreateIcon(false);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(FormWidth, 440);
        Padding = new Padding(12);

        BuildPages();
        BuildChrome();

        _gbePathBox.Text = GbeBinaryManager.CacheRoot;
        _gbePathBox.TextChanged += (_, _) => UpdateGbeDownloadState();
        UpdateGbeDownloadState();

        _advancedToggle.Click += (_, _) => SetAdvancedExpanded(!_advancedExpanded);
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
            c.Visible = expanded;
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

    // --- Layout ---

    private void BuildChrome()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(_content, 0, 0);

        _buttonBar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        _closeButton.Click += OnCloseClick;
        _nextButton.Click += OnNextClick;
        _backButton.Click += (_, _) => { if (_stepIndex > 0) { _stepIndex--; ShowStep(); } };
        _buttonBar.Controls.AddRange(new Control[] { _nextButton, _closeButton, _backButton });
        root.Controls.Add(_buttonBar, 0, 1);

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
            + "1601580 is Frostpunk 2. You can read it from the game's Steam store URL (the number in "
            + "store.steampowered.com/app/<id>/) or from SteamDB.",
            ("Look up a game on SteamDB", "https://steamdb.info/"),
            g =>
            {
                _appIdNote.Margin = new Padding(0, 0, 0, 6);
                g.Controls.Add(_appIdNote);
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

                g.Controls.Add(_backupCheck);
                g.Controls.Add(MakeHelp(
                    "To track achievements, the overlay replaces the game's Steam library (steam_api64.dll) with GBE's "
                    + "drop-in version. GBE emulates the Steam API locally and records each unlock to a file this app watches, "
                    + "so tracking works without Steam running.\r\n\r\n"
                    + "With this on, the original library is first copied to steam_api64.dll.original — keep it so you can "
                    + "restore the game's normal Steam integration later (it's also the file the tool reads to generate the "
                    + "interface list GBE needs)."));

                g.Controls.Add(_advancedToggle);

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
            "This can take a minute the first time, while the GBE emulator is downloaded.",
            null,
            g =>
            {
                _stepsTable = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
                _stepsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
                _stepsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                _stepsHost = new Panel { AutoScroll = true, Height = 150, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 4, 0, 0) };
                _stepsHost.Controls.Add(_stepsTable);
                g.Controls.Add(_stepsHost);

                g.Controls.Add(new Label { Text = "Details", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 6, 0, 2) });
                _logBox.Margin = new Padding(0, 0, 0, 0);
                g.Controls.Add(_logBox);
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

    private LinkLabel MakeLink(string text, string url)
    {
        var link = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
        link.LinkClicked += (_, _) => OpenUrl(url);
        return link;
    }

    // --- Navigation ---

    private void ShowStep()
    {
        var page = _steps[_stepIndex];
        ShowPage(page);
        _backButton.Visible = _stepIndex > 0;
        _nextButton.Visible = true;
        _nextButton.Text = page == Page.Ready ? "Add game" : "Next";
        _closeButton.Text = "Cancel";
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
        // Per-monitor DPI changed (moved to another monitor or scale changed) — re-fit the page,
        // but keep the window where the user put it (don't recenter, or it jumps back).
        BeginInvoke(() => FitToActivePage(recenter: false));
    }

    /// <summary>Wraps help text to the current width and resizes the dialog to the active page's height.</summary>
    private void FitToActivePage(bool recenter = true)
    {
        if (!IsHandleCreated || _activePanel?.Tag is not Control grid)
            return;

        var wrap = Math.Max(200, ClientSize.Width - Padding.Horizontal);
        foreach (var label in _wrapLabels)
            label.MaximumSize = new Size(wrap, 0);

        // Lay out at the current width, then size the dialog so the page's grid fits exactly.
        // (The grid's PreferredSize under-measures stacked wrapped labels, but its realized
        // height is correct.) Measure the fixed chrome around the content — form padding plus the
        // button-bar row, including its margin — from the realized layout rather than adding it up
        // by hand: the content panel and the grid both dock-fill/dock-top the same width, so the
        // client height that makes the content exactly as tall as the grid is grid.Height plus
        // whatever the client currently spends on non-content. A hand-summed constant used to
        // under-count this by a few px, clipping the last line of a label that had just wrapped
        // (e.g. the two-line "no Steam library found" status on the folder page).
        PerformLayout();
        var nonContentHeight = ClientSize.Height - _content.Height;
        var desired = grid.Height + nonContentHeight;
        var chrome = Height - ClientSize.Height;
        var maxClient = Screen.FromControl(this).WorkingArea.Height - chrome - 16;

        // Normally the dialog is sized exactly to its content (no scrollbar). If the content is
        // taller than the screen (small resolution / high zoom), clamp to the screen and let the
        // page scroll so nothing is unreachable.
        var clamped = desired > maxClient;
        ((Panel)_activePanel!).AutoScroll = clamped;
        ClientSize = new Size(ClientSize.Width, clamped ? maxClient : desired);
        if (recenter)
            CenterToScreen();
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
                if (string.IsNullOrWhiteSpace(_appIdBox.Text))
                {
                    MessageBox.Show(this, "Enter the game's Steam AppID.", "Add game", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                var newAppId = _appIdBox.Text.Trim();
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
            await ProceedAfterInputsAsync();
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
            var dlls = await Task.Run(() => DllLocator.FindAll(gameDir));
            var primary = DllLocator.SelectPrimary(dlls);
            if (primary == null)
            {
                _folderStatus.ForeColor = Color.Firebrick;
                _folderStatus.Text = "No steam_api64.dll / steam_api.dll found under this folder. Pick the game's install folder. This wizard configures Steam games only — other emulators are tracked automatically once they write to the GSE Saves folder.";
                FitToActivePage();
                return;
            }

            _gameDir = gameDir;
            _dllPrimaryPath = primary.Path;

            // If a config already exists at the target, confirm overwrite up front (not at the end).
            var existing = Path.Combine(Path.GetDirectoryName(primary.Path)!, "steam_settings");
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

            var localAppId = await Task.Run(() => AppIdResolver.FromAppIdTxt(gameDir) ?? AppIdResolver.FromIniFiles(gameDir));
            string? guessed = null;
            if (localAppId == null)
            {
                _folderStatus.Text = "Searching the Steam store…";
                var name = Path.GetFileName(gameDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
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
                await ProceedAfterInputsAsync(); // AppID + key already known — fetch, then ready/cookie
            else
            {
                _stepIndex = 1;
                ShowStep();
            }
        }
        catch (Exception ex)
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
            Force = force
        };

        _logBox.Clear();
        ResetSteps();
        ShowPage(Page.Progress);
        _backButton.Visible = false;
        _nextButton.Visible = false;
        FitToActivePage();
        _running = true;
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
            FinishRun();
            return;
        }
        catch (Exception ex)
        {
            Report(ConfigLogLevel.Error, ex.Message);
            FinishRun();
            return;
        }

        if (status is ConfigStatus.Success or ConfigStatus.PartialSuccess)
        {
            try
            {
                _onGameConfigured(_gameDir);
            }
            catch (Exception ex)
            {
                Report(ConfigLogLevel.Error, $"Game configured, but updating the overlay failed: {ex.Message}");
            }
        }

        Report(ConfigLogLevel.Info, "");
        Report(ConfigLogLevel.Info, status switch
        {
            ConfigStatus.Success => "✓ Game added. The overlay is now tracking it.",
            ConfigStatus.PartialSuccess => "✓ Game added with warnings (see above). The overlay is now tracking it.",
            ConfigStatus.UserError => "✗ Could not add the game — go back and fix the issue above.",
            ConfigStatus.DefenderBlocked => "✗ Windows Defender blocked GBE — see the options above.",
            _ => "✗ Failed — see the error above."
        });

        var success = status is ConfigStatus.Success or ConfigStatus.PartialSuccess;
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
            return;
        }
        base.OnFormClosing(e);
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

    private void PickFolder(TextBox target)
    {
        var picked = DialogControls.PickFolder(this, target.Text);
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
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
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
        Logger.Info($"[add-game] {level}: {message}");

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
        _stepsHost.ScrollControlIntoView(label);

        _currentStepIcon = icon;
        _currentStepLabel = label;
        _spinnerFrame = 0;
        _spinner.Start();
    }

    /// <summary>Marks the running step done (✓) or failed (✗) and stops the spinner.</summary>
    private void FinishStep(bool ok)
    {
        if (_currentStepIcon == null)
            return;
        _currentStepIcon.Font = new Font("Segoe UI Symbol", 9f, FontStyle.Bold);
        _currentStepIcon.Text = ok ? "✓" : "✗"; // ✓ / ✗
        _currentStepIcon.ForeColor = ok ? Color.SeaGreen : Color.Firebrick;
        _currentStepIcon = null;
        _currentStepLabel = null;
        _spinner.Stop();
    }

    /// <summary>Updates the running step's text in place (e.g. with a percentage).</summary>
    void IConfigProgress.UpdateStep(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ((IConfigProgress)this).UpdateStep(text));
            return;
        }
        if (_currentStepLabel != null)
            _currentStepLabel.Text = text;
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
        }
        base.Dispose(disposing);
    }
}
