using System.Globalization;
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

    // Every use of _writer takes this lock: the UI thread and the watcher's background tasks all log.
    private static readonly Lock _writerLock = new();

    /// <summary>The log file, next to the executable. Read this rather than rebuilding the path.</summary>
    public static string LogPath => Path.Combine(AppContext.BaseDirectory, "overlay.log");

    /// <summary>Where <see cref="LogPath"/> is moved once it outgrows <see cref="MaxLogBytes"/>.</summary>
    private static string PreviousLogPath => LogPath + ".1";

    /// <summary>
    /// Why <see cref="Init"/> could not open the log, or null when it could. Kept here because a
    /// logger that failed has nowhere to write its own failure; a diagnostic report carries it.
    /// </summary>
    public static string? InitError { get; private set; }

    public static void Init()
    {
        try
        {
            RollIfOversized();

            // Append, not truncate. The app starts with Windows for most users, so a reboot is a
            // launch: truncating here destroyed the session the bug report was about and left behind
            // a file that reads like a clean run rather than one saying the evidence is gone.
            lock (_writerLock)
            {
                _writer = new StreamWriter(LogPath, append: true) { AutoFlush = true };

                // Session banner. An appended log has no boundaries without it, and WarnOnce dedupes per
                // process — so a reader needs to know where one run ends for a missing warning to mean
                // "not this time" rather than "already said".
                _writer.WriteLine($"{SessionBannerPrefix} {Timestamp()}, {AppUtilities.InformationalVersion} =====");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            InitError = ex.Message;
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    public static void Close()
    {
        lock (_writerLock)
        {
            _writer?.Dispose();
            _writer = null;
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Rolling is a nicety; failing it must not cost this session its logging.
        }
    }

    private static void Write(string level, string message)
    {
        lock (_writerLock)
            _writer?.WriteLine($"[{Timestamp()}] [{level}] {message}");
    }

    // Invariant: under th-TH the current culture writes the year as 2569, and fi-FI writes the time as 02.00.00.
    private static string Timestamp() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
