namespace AchievementOverlay.GbeConfig;

/// <summary>Severity of a progress message emitted during config generation.</summary>
public enum ConfigLogLevel
{
    Debug,
    Info,
    Step,
    Warning,
    Error
}

/// <summary>
/// Sink for progress messages from <see cref="GbeConfigGenerator"/>. The dialog
/// implements this to append to its log view; tests can capture into a list.
/// </summary>
public interface IConfigProgress
{
    void Report(ConfigLogLevel level, string message);

    /// <summary>Updates the text of the current step in place (e.g. "Extracting… 42%") without
    /// adding a new step or log line. No-op if there's no active step.</summary>
    void UpdateStep(string text);

    /// <summary>Marks the current step as failed (✗) — used for non-fatal step failures where the
    /// run continues (e.g. the SteamDB scrape). Logs <paramref name="message"/> as a warning.</summary>
    void FailStep(string message);
}

/// <summary>Final outcome of a config generation run.</summary>
public enum ConfigStatus
{
    /// <summary>Everything completed.</summary>
    Success,

    /// <summary>Config was written but something non-essential failed (e.g. hidden descriptions, an icon).</summary>
    PartialSuccess,

    /// <summary>Bad input or a precondition that the user must fix (missing DLL, Denuvo with no crack, etc.).</summary>
    UserError,

    /// <summary>Windows Defender blocked the GBE download (a known false positive). The front-end can
    /// offer to add Defender exclusions and let the user retry.</summary>
    DefenderBlocked,

    /// <summary>A network or filesystem failure.</summary>
    Failure
}
