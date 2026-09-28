using Xunit;

namespace AchievementOverlay.Tests;

/// <summary>
/// Every test class that opens the app log — through <see cref="AppLog.While"/>, which calls
/// <see cref="Logger.Init"/> — joins this collection. The logger keeps one
/// writer per process: an Init while another test holds the file open fails to open it, and a Close
/// ends the other test's logging mid-run. The collection runs apart from every other one, so a class
/// that only logs does not have to join it.
/// </summary>
[CollectionDefinition("App log", DisableParallelization = true)]
public sealed class AppLogCollection
{
}

/// <summary>The app's own log, opened around part of a test in <see cref="AppLogCollection"/>.</summary>
public static class AppLog
{
    /// <summary>Runs <paramref name="act"/> with the app's own log open, as it is for the whole of a session.</summary>
    public static void While(Action act)
    {
        Logger.Init();
        try
        {
            act();
        }
        finally
        {
            Logger.Close();
        }
    }
}
