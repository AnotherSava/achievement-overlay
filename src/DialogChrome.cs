using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace AchievementOverlay;

/// <summary>
/// The chrome the Fluent theme has no key for, shared by every WPF dialog in the app. Kept here
/// rather than in one window because a second window carrying its own copy of these colours is a
/// second copy that drifts: a shade corrected in one dialog would leave the other looking wrong, with
/// nothing to say why.
/// </summary>
public static class DialogChrome
{
    /// <summary>
    /// Supplies the page, card and nav colours, matched to whichever mode Windows is in. Everything
    /// else comes from <c>ThemeMode="System"</c>. Written into the window's own resources, so a
    /// <c>DynamicResource</c> lookup finds these ahead of anything a merged dictionary defines.
    /// </summary>
    public static void ApplyThemeBrushes(ResourceDictionary resources)
    {
        var dark = IsSystemInDarkMode();
        void Set(string key, string hex) =>
            resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);

        Set("WindowBackground", dark ? "#202020" : "#F3F3F3");
        Set("PageBackground", dark ? "#272727" : "#FBFBFB");
        Set("CardBackground", dark ? "#2D2D2D" : "#FDFDFD");
        Set("CardBorder", dark ? "#3A3A3A" : "#E5E5E5");
        Set("NavSelected", dark ? "#333333" : "#EBEBEB");
        Set("NavHover", dark ? "#2F2F2F" : "#F0F0F0");
        Set("Accent", dark ? "#60CDFF" : "#005FB8");
        Set("SwitchOff", dark ? "#333333" : "#FFFFFF");
        Set("SwitchKnobOff", dark ? "#CCCCCC" : "#5D5D5D");
        // A notice band. Its own tint rather than NavSelected: that is the selected nav row's colour,
        // and a notice wearing it reads as another selected row.
        Set("NoticeBackground", dark ? "#1F2A33" : "#F2F7FC");
        Set("NoticeBorder", dark ? "#2F4152" : "#CFE1F2");
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
            return false; // unreadable registry is not a reason to fail to open a dialog
        }
    }

    /// <summary>WPF wants an ImageSource, so the embedded .ico is decoded rather than reused as a GDI icon.</summary>
    public static void LoadWindowIcon(Window window)
    {
        try
        {
            using var stream = typeof(DialogChrome).Assembly.GetManifestResourceStream("AchievementOverlay.icon.ico");
            if (stream != null)
                window.Icon = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not load the window icon: {ex.Message}");
        }
    }

    /// <summary>
    /// Caps a window's height to the work area, so a height chosen to fit its tallest content does not
    /// open off-screen on a short display or under heavy display scaling.
    /// </summary>
    public static void ClampToScreen(Window window)
    {
        var available = SystemParameters.WorkArea.Height - 40;
        if (window.Height > available)
            window.Height = Math.Max(window.MinHeight, available);
    }
}
