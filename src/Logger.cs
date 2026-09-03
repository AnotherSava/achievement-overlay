using System.IO;

namespace AchievementOverlay;

public static class Logger
{
    /// <summary>
    /// Size past which the log is rolled aside at startup. At the few KB a session writes this keeps
    /// roughly a couple of hundred sessions — long enough that the run being reported is still
    /// present, bounded enough that the file stays attachable to an issue.
    /// </summary>
    private const long MaxLogBytes = 1024 * 1024;

    /// <summary>
    /// Start of the line every run opens with. A diagnostic report slices the log on this, so the two
    /// must agree about it — hence a constant rather than the literal written twice.
    /// </summary>
    public const string SessionBannerPrefix = "===== session started";

    private static StreamWriter? _writer;

    /// <summary>The log file, next to the executable. Read this rather than rebuilding the path.</summary>
    public static string LogPath => Path.Combine(AppContext.BaseDirectory, "overlay.log");

    /// <summary>Where <see cref="LogPath"/> is moved once it outgrows <see cref="MaxLogBytes"/>.</summary>
    private static string PreviousLogPath => LogPath + ".1";

    public static void Init()
    {
        try
        {
            RollIfOversized();

            // Append, not truncate. The app starts with Windows for most users, so a reboot is a
            // launch: truncating here destroyed the session the bug report was about and left behind
            // a file that reads like a clean run rather than one saying the evidence is gone.
            _writer = new StreamWriter(LogPath, append: true) { AutoFlush = true };

            // Session banner. An appended log has no boundaries without it, and WarnOnce dedupes per
            // process — so a reader needs to know where one run ends for a missing warning to mean
            // "not this time" rather than "already said".
            _writer.WriteLine($"{SessionBannerPrefix} {DateTime.Now:yyyy-MM-dd HH:mm:ss}, {AppUtilities.InformationalVersion} =====");
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

    /// <summary>
    /// The log's current contents, or an empty string if it cannot be read. Shares the file with the
    /// writer this class holds open, so callers need not <see cref="Close"/> first: a plain
    /// File.ReadAllText asks for a share mode that the writer's own handle denies.
    /// </summary>
    public static string ReadAll()
    {
        try
        {
            using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Moves an oversized log aside so the fresh one starts empty. A single generation on purpose:
    /// a deeper rotation buys nothing once the cap already holds hundreds of sessions.
    /// </summary>
    private static void RollIfOversized()
    {
        var info = new FileInfo(LogPath);
        if (!info.Exists || info.Length < MaxLogBytes)
            return;

        try
        {
            File.Move(LogPath, PreviousLogPath, overwrite: true);
        }
        catch (IOException)
        {
            // Rolling is a nicety; failing it must not cost this session its logging.
        }
    }

    private static void Write(string level, string message)
    {
        _writer?.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}");
    }
}
