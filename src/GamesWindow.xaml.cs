using System.IO;
using System.Windows;
using System.Windows.Controls;
using AchievementOverlay.GbeConfig;
using Button = System.Windows.Controls.Button;
using IWin32Window = System.Windows.Forms.IWin32Window;
using MessageBox = System.Windows.MessageBox;

namespace AchievementOverlay;

/// <summary>
/// Lists every game the app knows of, configured or only seen as a save folder, with where each one is
/// and how far it has got, and is where games are added and reverted. The window holds no state of its
/// own: every refresh asks the host for the games again, so an Add game run or a revert shows up at once.
/// </summary>
public sealed partial class GamesWindow : Window, IWin32Window
{
    private readonly AppConfig _config;
    private readonly Func<IReadOnlyList<GameChoice>> _loadGames;
    private readonly Action<IWin32Window, string?, string?> _addGame;
    private readonly Action _gamesChanged;

    /// <param name="config">Supplies the save folders unlock files are read from, and keeps the hidden columns.</param>
    /// <param name="loadGames">The games to list, read again on every refresh.</param>
    /// <param name="addGame">
    /// Runs the Add game wizard, owned by this window, and returns once it closes. Takes the install folder and
    /// AppID to start from, both null for a game the user has yet to pick.
    /// </param>
    /// <param name="gamesChanged">Tells the host a revert changed a game's files, so it rescans.</param>
    public GamesWindow(AppConfig config, Func<IReadOnlyList<GameChoice>> loadGames, Action<IWin32Window, string?, string?> addGame, Action gamesChanged)
    {
        _config = config;
        _loadGames = loadGames;
        _addGame = addGame;
        _gamesChanged = gamesChanged;

        InitializeComponent();
        DialogChrome.ApplyThemeBrushes(Resources);
        DialogChrome.ClampToScreen(this);
        DialogChrome.LoadWindowIcon(this);

        var hidden = config.GetCurrent().HiddenGamesColumns ?? [];
        foreach (var (key, column) in ToggleableColumns())
            column.Visibility = hidden.Contains(key, StringComparer.Ordinal) ? Visibility.Collapsed : Visibility.Visible;

        Loaded += async (_, _) => await RefreshAsync();
    }

    public IntPtr Handle => new System.Windows.Interop.WindowInteropHelper(this).Handle;

    /// <summary>
    /// The columns Columns… can hide, by the key config stores them under. The game's name and the ⋯ actions button
    /// are left out: a row without either has nothing to identify it or to act on.
    /// </summary>
    private (string Key, DataGridColumn Column)[] ToggleableColumns() =>
    [
        ("config", ConfigColumn), ("emulator", EmulatorColumn), ("achievements", AchievementsColumn), ("lastUnlock", LastUnlockColumn),
        ("appId", AppIdColumn), ("gameFolder", GameFolderColumn), ("saveFolder", SaveFolderColumn)
    ];

    /// <summary>Drops a WinForms menu below the button, so it looks like the tray's.</summary>
    private void OnColumns(object sender, RoutedEventArgs e)
    {
#pragma warning disable CA2000 // Disposed by its own Closed handler below: Show returns while the menu is still open
        var menu = new System.Windows.Forms.ContextMenuStrip();
#pragma warning restore CA2000
        foreach (var (_, column) in ToggleableColumns())
        {
            // The config column draws only an icon, so its header is empty and the menu names it instead.
            var label = column == ConfigColumn ? "Config check" : (string)column.Header;
            var item = new System.Windows.Forms.ToolStripMenuItem(label) { CheckOnClick = true, Checked = column.Visibility == Visibility.Visible };
            item.CheckedChanged += (_, _) =>
            {
                column.Visibility = item.Checked ? Visibility.Visible : Visibility.Collapsed;
                SaveHiddenColumns();
            };
            menu.Items.Add(item);
        }

        // Stays open on a click, so several columns can be ticked in one visit.
        menu.Closing += (_, args) => args.Cancel = args.CloseReason == System.Windows.Forms.ToolStripDropDownCloseReason.ItemClicked;
        ShowBelow(menu, (FrameworkElement)sender);
    }

    /// <summary>
    /// Drops a WinForms menu below <paramref name="anchor"/>: the kind the tray uses, so the app's menus look alike.
    /// A click elsewhere closes it because Program routes the WPF loop's messages to WinForms. Disposed once it has
    /// closed, after any click it raised has been handled.
    /// </summary>
    private void ShowBelow(System.Windows.Forms.ContextMenuStrip menu, FrameworkElement anchor)
    {
        var below = anchor.PointToScreen(new Point(0, anchor.ActualHeight));
        menu.Closed += (_, _) => Dispatcher.BeginInvoke(menu.Dispose);
        menu.Show(new System.Drawing.Point((int)below.X, (int)below.Y));
    }

    /// <summary>The row's menu: its two folders, then Set up… or Revert…, each greyed out with the reason when it cannot run.</summary>
    private void OnRowMenu(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: GameRow row } button)
            return;

#pragma warning disable CA2000 // Disposed by ShowBelow once it has closed: Show returns while the menu is still open
        var menu = new System.Windows.Forms.ContextMenuStrip { ShowItemToolTips = true };
#pragma warning restore CA2000
        AddItem("Open game folder", row.GameFolder.Length > 0 ? null : "No install folder is known for this game.", () => OpenFolder(row.GameFolder));
        AddItem("Open save folder", row.SaveFolder.Length > 0 ? null : "The game has no save folder yet.", () => OpenFolder(row.SaveFolder));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        AddItem(row.ActionLabel, row.ActionEnabled ? null : row.ActionTooltip, async () => await RunRowActionAsync(row));
        ShowBelow(menu, button);

        void AddItem(string text, string? blocker, Action run)
        {
            var item = new System.Windows.Forms.ToolStripMenuItem(text) { Enabled = blocker == null, ToolTipText = blocker };
            item.Click += (_, _) => run();
            menu.Items.Add(item);
        }
    }

    private void SaveHiddenColumns()
    {
        var hidden = ToggleableColumns().Where(entry => entry.Column.Visibility != Visibility.Visible).Select(entry => entry.Key).ToList();
        try
        {
            _config.UpdateConfigValue(nameof(SettingsData.HiddenGamesColumns), hidden);
        }
        catch (ConfigSaveException ex)
        {
            // Only the next window's columns depend on it; this one already shows the choice.
            Logger.Warn($"Could not save the Games window's columns: {ex.Message}");
        }
    }

    private async Task RefreshAsync()
    {
        Status.Text = "Checking games…";
        var games = _loadGames();
        var savesPaths = _config.GseSavesPaths;
        var gamesPaths = _config.GamesPaths;
        var now = DateTimeOffset.Now; // one clock read, so rows written by the same moment agree
        List<GameRow> rows;
        try
        {
            rows = await Task.Run(() =>
            {
                var installFolders = GameList.FindInstallFolders(gamesPaths);
                return GameList.WithInstalledGames(games, installFolders).Select(game => GameList.Describe(game, savesPaths, installFolders, now)).ToList();
            });
        }
#pragma warning disable CA1031 // Window boundary: logs at Error and says so in the window, which stays usable for Add game
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Logger.Error($"Games window could not list the games: {ex}");
            Status.Text = $"Could not list the games: {ex.Message}";
            return;
        }
        Grid.ItemsSource = rows;

        var unconfigured = rows.Count(row => row.Choice.Unconfigured);
        Status.Text = AppUtilities.CountOf(rows.Count, "game") + (unconfigured > 0 ? $", {unconfigured} not set up" : "");
    }

    private void OnFolderMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement { Tag: string { Length: > 0 } folder })
            OpenFolder(folder);
    }

    private void OpenFolder(string folder)
    {
        // Explorer given a folder that is gone opens Documents instead, which reads as the wrong folder.
        if (!Directory.Exists(folder))
        {
            MessageBox.Show(this, $"This folder no longer exists:\n\n{folder}", "Open in Explorer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        System.Diagnostics.Process.Start("explorer.exe", $"\"{folder}\"")?.Dispose();
    }

    private async void OnAddGame(object sender, RoutedEventArgs e)
    {
        _addGame(this, null, null);
        await RefreshAsync();
    }

    private async Task RunRowActionAsync(GameRow row)
    {
        if (row.Choice.Unconfigured)
        {
            _addGame(this, row.GameFolder.Length > 0 ? row.GameFolder : null, row.AppId);
            await RefreshAsync();
            return;
        }

        if (row.SettingsDir is not { } settingsDir)
            return;

        var recorded = File.Exists(Path.Combine(settingsDir, SetupRecord.FileName));
        var what = recorded
            ? "This puts back the game's own Steam library, removes the files Add game wrote and restores any it replaced."
            : "This puts back the game's own Steam library. Its achievement config is kept, since this game was set up before Add game recorded which files it wrote.";
        var answer = MessageBox.Show(this, $"Revert {row.Name}?\n\n{what} The game's unlock history is kept.\n\nClose the game first.",
            "Revert game", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK)
            return;

        RevertResult result;
        try
        {
            result = GameReverter.Revert(settingsDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            Logger.Error($"Revert failed for appid={row.AppId}: {ex.Message}");
            MessageBox.Show(this, $"{row.Name} could not be reverted. If the game is running, close it and try again.\n\n{ex.Message}",
                "Revert game", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        foreach (var line in result.Done.Concat(result.Kept))
            Logger.Info($"Revert appid={row.AppId}: {line}");
        _gamesChanged();
        await RefreshAsync();

        MessageBox.Show(this, $"{row.Name} is reverted.\n\n{string.Join("\n", result.Done.Concat(result.Kept))}",
            "Revert game", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
