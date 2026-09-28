using System.IO;

namespace AchievementOverlay.GbeConfig;

/// <summary>
/// Decides whether a newly configured game falls under one of the already-configured
/// <c>gamesPaths</c> roots, and if not, which root to add so the overlay's
/// <see cref="GameCache"/> picks it up.
/// </summary>
public static class GamesPathPlanner
{
    /// <summary>
    /// Returns the directory to add to <c>gamesPaths</c> so <paramref name="gameDir"/> is
    /// scanned, or null if an existing root already covers it. The added root is the
    /// game's parent folder (so the game's own folder name becomes the cache's GameName);
    /// a game folder that is itself a drive root has no parent, so it is returned as it is.
    /// </summary>
    public static string? PlanRootToAdd(IEnumerable<string> existingPaths, string gameDir)
    {
        var game = FolderPath.Parse(gameDir);

        if (existingPaths.Where(existing => !string.IsNullOrWhiteSpace(existing)).Any(existing => FolderPath.Parse(existing).Contains(game)))
            return null;

        return Directory.GetParent(game.ToString())?.FullName ?? game.ToString();
    }
}
