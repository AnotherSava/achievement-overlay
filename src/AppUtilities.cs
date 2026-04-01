using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;

namespace AchievementOverlay;

/// <summary>
/// Shared utilities: logging, icon management, screen geometry.
/// </summary>
public static class AppUtilities
{
    // --- Logging ---

    public static StreamWriter? InitLog()
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "achievement-overlay.log");
            return new StreamWriter(logPath, append: false) { AutoFlush = true };
        }
        catch
        {
            return null;
        }
    }

    public static void Log(StreamWriter? writer, string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        writer?.WriteLine(line);
    }

    // --- Icon management ---

    public static Icon LoadOrCreateIcon(bool grayscale)
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AchievementOverlay.icon.ico");
        if (stream != null)
        {
            var icon = new Icon(stream);
            if (!grayscale) return icon;

            using (icon)
            using (var bmp = icon.ToBitmap())
            using (var grayBmp = ToGrayscale(bmp))
            {
                return CloneIconFromHandle(grayBmp.GetHicon());
            }
        }

        return CreateDefaultIcon(grayscale);
    }

    private static Icon CreateDefaultIcon(bool grayscale)
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var fillColor = grayscale ? Color.Gray : Color.FromArgb(0xDA, 0xA5, 0x20);
        var borderColor = grayscale ? Color.DarkGray : Color.FromArgb(0xFF, 0xD7, 0x00);

        using var fillBrush = new SolidBrush(fillColor);
        using var borderPen = new Pen(borderColor, 1);
        g.FillEllipse(fillBrush, 1, 1, 13, 13);
        g.DrawEllipse(borderPen, 1, 1, 13, 13);

        var starColor = grayscale ? Color.LightGray : Color.FromArgb(0xFF, 0xF8, 0xDC);
        using var font = new Font("Segoe UI", 7f, System.Drawing.FontStyle.Bold);
        using var starBrush = new SolidBrush(starColor);
        var starSize = g.MeasureString("\u2605", font);
        g.DrawString("\u2605", font, starBrush, (16 - starSize.Width) / 2, (16 - starSize.Height) / 2);

        return CloneIconFromHandle(bmp.GetHicon());
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
                var primaryDpiScale = SystemParameters.PrimaryScreenHeight > 0
                    ? Screen.PrimaryScreen!.Bounds.Height / SystemParameters.PrimaryScreenHeight
                    : 1.0;
                return new Rect(wa.Left / primaryDpiScale, wa.Top / primaryDpiScale, wa.Width / primaryDpiScale, wa.Height / primaryDpiScale);
            }
        }
        catch
        {
            // Fall through to default
        }

        var area = SystemParameters.WorkArea;
        return new Rect(0, 0, area.Width, area.Height);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
