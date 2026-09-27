using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;

namespace AchievementOverlay;

/// <summary>
/// Shared utilities: app version, icon management, screen geometry.
/// </summary>
public static class AppUtilities
{
    // --- App version ---

    /// <summary>The SDK's default version, which is what a build with no version passed in carries.</summary>
    private const string DevVersion = "1.0.0";

    /// <summary>
    /// The assembly's informational version, commit suffix included (e.g. <c>1.9.1+3d0303b</c>).
    /// Only a release passes a real version in, so a local build reports <see cref="DevVersion"/>.
    /// </summary>
    public static string InformationalVersion =>
        typeof(AppUtilities).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? DevVersion;

    /// <summary>
    /// The same version as a label for people: <c>v1.9.1</c>, or <c>dev version</c> for a local build.
    /// The commit suffix is dropped here and kept in <see cref="InformationalVersion"/>, which is what
    /// the log banner and a diagnostic report carry — <c>1.9.1</c> alone cannot say which build it was.
    /// </summary>
    public static string VersionLabel
    {
        get
        {
            var version = InformationalVersion.Split('+')[0];
            return version == DevVersion ? "dev version" : $"v{version}";
        }
    }

    // --- Filesystem scanning ---

    /// <summary>
    /// Options for every recursive scan of a game folder. The default <c>AttributesToSkip</c> is
    /// <c>Hidden | System</c>, which quietly skips a hidden <c>steam_settings</c> — and repacks hide
    /// theirs routinely, so a game whose only config folder was hidden went untracked with nothing in
    /// the log to say why. System stays skipped: that is what keeps a scan out of
    /// <c>$RECYCLE.BIN</c> and <c>System Volume Information</c>, which are hidden <em>and</em> system.
    /// A fresh instance each time because the type is mutable.
    /// </summary>
    public static EnumerationOptions RecursiveScan => new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.System
    };

    // --- Icon management ---

    /// <summary>The app icon's manifest resource name, as the csproj's <c>LogicalName</c> gives it.</summary>
    internal const string IconResourceName = "AchievementOverlay.icon.ico";

    public static Icon LoadAppIcon(bool grayscale)
    {
        // Icon(Stream) reads the whole stream into its own buffer, so the icon outlives the stream.
        using var stream = EmbeddedAssets.Open(IconResourceName);
        var icon = new Icon(stream);
        if (!grayscale) return icon;

        using (icon)
        using (var bmp = icon.ToBitmap())
        using (var grayBmp = ToGrayscale(bmp))
        {
            return CloneIconFromHandle(grayBmp.GetHicon());
        }
    }

    private static Bitmap ToGrayscale(Bitmap source)
    {
        var gray = new Bitmap(source.Width, source.Height);
        for (var x = 0; x < source.Width; x++)
        {
            for (var y = 0; y < source.Height; y++)
            {
                var pixel = source.GetPixel(x, y);
                var lum = (int)(pixel.R * 0.299 + pixel.G * 0.587 + pixel.B * 0.114);
                gray.SetPixel(x, y, Color.FromArgb(pixel.A, lum, lum, lum));
            }
        }
        return gray;
    }

    private static Icon CloneIconFromHandle(IntPtr hIcon)
    {
        using var tempIcon = Icon.FromHandle(hIcon);
        var clone = (Icon)tempIcon.Clone();
        DestroyIcon(hIcon);
        return clone;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    // --- Screen geometry ---

    /// <summary>
    /// Gets the work area of the monitor containing the foreground window.
    /// Converts physical pixels to WPF DIPs using the primary monitor's DPI scale.
    /// </summary>
    public static Rect GetForegroundWindowRect()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                var screen = Screen.FromHandle(hwnd);
                var wa = screen.WorkingArea;
                // Convert physical pixels using THIS monitor's own DPI — that's the coordinate space
                // WPF uses for Window.Left/Top (the window's own monitor), so placement is correct
                // on every display regardless of the primary monitor's scale.
                var scale = GetMonitorScale(hwnd);
                return new Rect(wa.Left / scale, wa.Top / scale, wa.Width / scale, wa.Height / scale);
            }
        }
        catch
        {
            // Fall through to default
        }

        var area = SystemParameters.WorkArea;
        return new Rect(0, 0, area.Width, area.Height);
    }

    /// <summary>
    /// Width of the foreground window's monitor work area in that monitor's OWN logical units
    /// (physical pixels ÷ that monitor's DPI scale). Used to size the popup proportionally to the
    /// display it actually appears on, independent of the primary monitor's DPI/zoom.
    /// </summary>
    public static double GetForegroundLogicalWidth()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                var screen = Screen.FromHandle(hwnd);
                return screen.WorkingArea.Width / GetMonitorScale(hwnd);
            }
        }
        catch
        {
            // Fall through to default
        }

        return SystemParameters.WorkArea.Width;
    }

    /// <summary>
    /// Effective DPI scale (1.0 = 100%) of the monitor containing the given window, queried per-monitor
    /// so it reflects that display's actual zoom regardless of any process's DPI awareness.
    /// </summary>
    private static double GetMonitorScale(IntPtr hwnd)
    {
        var hmon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (hmon != IntPtr.Zero && GetDpiForMonitor(hmon, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
            return dpiX / 96.0;
        return 1.0;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
}
