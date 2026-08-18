using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AchievementOverlay;

/// <summary>
/// Controls and pickers shared by the app's dialogs, so a browse button in one can't drift away
/// from the same button in the other.
/// </summary>
internal static class DialogControls
{
    /// <summary>
    /// A frameless square button carrying the OS's own folder glyph. Its height is left to the host
    /// form's OnLoad, which matches it to the text box it sits next to.
    /// </summary>
    public static Button MakeBrowseButton(ToolTip toolTip, string tip, Action onClick)
    {
        var button = new Button
        {
            AutoSize = false,
            Width = 24,
            Height = 22,
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0),
            TabStop = false,
            ImageAlign = ContentAlignment.MiddleCenter,
            BackColor = SystemColors.Control,
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = SystemColors.ControlLight;
        button.FlatAppearance.MouseDownBackColor = SystemColors.ControlDark;

        var icon = NativeFolderIcon.Shared;
        if (icon != null)
            button.Image = icon;
        else
            button.Text = "…";

        toolTip.SetToolTip(button, tip);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>A full-width input row with a trailing browse button.</summary>
    public static Control MakeInputRow(TextBox input, Button trailing)
    {
        var row = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 2, 0, 2) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        input.Margin = new Padding(0, 0, 4, 0);
        input.Dock = DockStyle.Fill;
        row.Controls.Add(input, 0, 0);
        row.Controls.Add(trailing, 1, 0);
        return row;
    }

    /// <summary>
    /// Shows the folder picker and returns the chosen path, or null if it was cancelled. The owner
    /// is optional so the WPF settings window, which has no IWin32Window, can use the same picker.
    /// </summary>
    public static string? PickFolder(IWin32Window? owner, string? initialDir)
    {
        using var dialog = new FolderBrowserDialog { ShowNewFolderButton = false };
        if (!string.IsNullOrEmpty(initialDir) && Directory.Exists(initialDir))
            dialog.SelectedPath = initialDir;
        var result = owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return result == DialogResult.OK ? dialog.SelectedPath : null;
    }
}

/// <summary>Loads the OS's native shell folder icon so browse buttons match Windows.</summary>
internal static class NativeFolderIcon
{
    private const int SiidFolder = 3;
    private const uint ShgsiIcon = 0x000000100;
    private const uint ShgsiSmallIcon = 0x000000001;

    private static Bitmap? _shared;
    private static bool _loaded;

    /// <summary>
    /// The folder glyph, loaded once and kept for the life of the process — every dialog draws the
    /// same 16px bitmap, so handing each one its own copy to dispose only invites a use-after-free.
    /// </summary>
    public static Bitmap? Shared
    {
        get
        {
            if (!_loaded)
            {
                _loaded = true;
                _shared = GetSmall();
            }
            return _shared;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShStockIconInfo
    {
        public uint cbSize;
        public IntPtr hIcon;
        public int iSysImageIndex;
        public int iIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szPath;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetStockIconInfo(int siid, uint uFlags, ref ShStockIconInfo psii);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static Bitmap? GetSmall()
    {
        var info = new ShStockIconInfo { cbSize = (uint)Marshal.SizeOf<ShStockIconInfo>() };
        try
        {
            if (SHGetStockIconInfo(SiidFolder, ShgsiIcon | ShgsiSmallIcon, ref info) != 0 || info.hIcon == IntPtr.Zero)
                return null;
            using var icon = Icon.FromHandle(info.hIcon);
            return icon.ToBitmap();
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (info.hIcon != IntPtr.Zero)
                DestroyIcon(info.hIcon);
        }
    }
}
