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

    /// <summary>
    /// The deepest <c>gamesPaths</c> entry containing a game chosen at <paramref name="gameDir"/>, or null
    /// when none does. Remembered as the Add game wizard's next browse start, which is why only an entry
    /// already listed qualifies and the wizard calls this when the folder is chosen, before any run can register the game: config
    /// travels in a diagnostic report, and a folder planned rather than listed can be a repack wrapper
    /// named after the game.
    /// </summary>
    public static string? GamesRootOf(IEnumerable<string> existingPaths, string gameDir) => DeepestContaining(existingPaths, gameDir);

    /// <summary>
    /// Where the wizard's folder picker opens when no folder has been typed: the only games root when
    /// there is one, otherwise the root containing <paramref name="lastGamesRoot"/>, otherwise null
    /// (the picker's own default), since with several roots nothing else says which one is meant.
    /// </summary>
    public static string? ChooseBrowseStart(IReadOnlyList<string> gamesPaths, string? lastGamesRoot)
    {
        if (gamesPaths.Count == 1)
            return gamesPaths[0];
        return string.IsNullOrWhiteSpace(lastGamesRoot) ? null : DeepestContaining(gamesPaths, lastGamesRoot);
    }

    private static string? DeepestContaining(IEnumerable<string> paths, string folder)
    {
        var target = FolderPath.Parse(folder);
        return paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => (Raw: p, Path: FolderPath.Parse(p))).Where(p => p.Path.Contains(target)).OrderByDescending(p => p.Path.Names.Count).Select(p => p.Raw).FirstOrDefault();
    }
}
