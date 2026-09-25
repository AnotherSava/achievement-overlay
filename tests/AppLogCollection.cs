using Xunit;

namespace AchievementOverlay.Tests;

/// <summary>
/// Every test class that calls <see cref="Logger.Init"/> joins this collection. The logger keeps one
/// writer per process: an Init while another test holds the file open fails to open it, and a Close
/// ends the other test's logging mid-run. The collection runs apart from every other one, so a class
/// that only logs does not have to join it.
/// </summary>
[CollectionDefinition("App log", DisableParallelization = true)]
public sealed class AppLogCollection
{
}
