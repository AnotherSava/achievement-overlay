using System.IO;

namespace AchievementOverlay;

public static class Logger
{
    private static StreamWriter? _writer;

    public static void Init()
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "achievement-overlay.log");
            _writer = new StreamWriter(logPath, append: false) { AutoFlush = true };
        }
        catch
        {
            // Can't create log file — logging silently disabled
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    public static void Close()
    {
        _writer?.Dispose();
        _writer = null;
    }

    private static void Write(string level, string message)
    {
        _writer?.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}");
    }
}
