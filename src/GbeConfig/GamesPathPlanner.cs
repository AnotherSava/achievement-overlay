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
    /// if the game sits at a drive root, the game folder itself is returned.
    /// </summary>
    public static string? PlanRootToAdd(IEnumerable<string> existingPaths, string gameDir)
    {
        var game = Normalize(gameDir);

        foreach (var existing in existingPaths)
        {
            if (string.IsNullOrWhiteSpace(existing))
                continue;
            if (Covers(Normalize(existing), game))
                return null;
        }

        var parent = Directory.GetParent(game)?.FullName;
        return parent != null ? Normalize(parent) : game;
    }

    /// <summary>True when <paramref name="root"/> equals or is an ancestor of <paramref name="path"/>.</summary>
    private static bool Covers(string root, string path)
    {
        if (string.Equals(root, path, StringComparison.OrdinalIgnoreCase))
            return true;
        var rootWithSep = root + Path.DirectorySeparatorChar;
        return path.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
