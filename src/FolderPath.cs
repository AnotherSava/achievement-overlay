using System.IO;

namespace AchievementOverlay;

/// <summary>
/// A path compared as its root plus the folder names below it, rather than as text. "Is this the
/// folder or inside it" is then a name-wise prefix test under one root: a drive root is an empty list
/// of names, so there is no separator to append to it, and <c>C:\GamesOther</c> is never inside
/// <c>C:\Games</c>, because names compare whole.
/// </summary>
/// <remarks>
/// Parsed where a path is used and never stored. It is built from a path whose environment variables
/// are already expanded — <see cref="AppConfig"/> is the one place that expands them — so config keeps
/// the spelling the user wrote, <c>%appdata%\GSE Saves</c> included.
/// </remarks>
public sealed class FolderPath
{
    private FolderPath(string root, string[] names)
    {
        Root = root;
        Names = names;
    }

    /// <summary>The root as <see cref="Path.GetPathRoot(string)"/> gives it: <c>D:\</c>, or <c>\\server\share</c>.</summary>
    private string Root { get; }

    /// <summary>The folder names below <see cref="Root"/>, outermost first. A root itself has none.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>
    /// Reads <paramref name="path"/> through <see cref="Path.GetFullPath(string)"/>, which resolves
    /// <c>..</c> and a relative path. Either separator splits a name from the next, and an empty name —
    /// left by a trailing or a doubled separator — is dropped.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty, only whitespace, or holds a character no path can.</exception>
    public static FolderPath Parse(string path)
    {
        var full = Path.GetFullPath(path);
        var root = full[..Path.GetPathRoot(full.AsSpan()).Length];
        var names = full[root.Length..].Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        return new FolderPath(root, names);
    }

    /// <summary>
    /// True when <paramref name="other"/> is this folder or sits anywhere inside it: the same root, and
    /// this folder's names leading <paramref name="other"/>'s. Both compare without case, as Windows does.
    /// </summary>
    public bool Contains(FolderPath other) =>
        string.Equals(Root, other.Root, StringComparison.OrdinalIgnoreCase)
        && other.Names.Count >= Names.Count
        && Names.SequenceEqual(other.Names.Take(Names.Count), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The name this path goes by under <paramref name="ancestor"/>: its first folder name below it, so
    /// <c>C:\Games\Aphelion\Engine\Win64</c> under <c>C:\Games</c> is <c>Aphelion</c>. A path that is
    /// <paramref name="ancestor"/> itself goes by that folder's own name, or by its root when it has none.
    /// </summary>
    /// <exception cref="ArgumentException">This path is not inside <paramref name="ancestor"/>.</exception>
    public string FirstNameBelow(FolderPath ancestor)
    {
        if (!ancestor.Contains(this))
            throw new ArgumentException($"'{this}' is not inside '{ancestor}'.", nameof(ancestor));

        if (Names.Count > ancestor.Names.Count)
            return Names[ancestor.Names.Count];
        return Names.Count > 0 ? Names[^1] : Root;
    }

    /// <summary>
    /// <paramref name="folders"/> without every entry another one already covers: an entry inside
    /// another is dropped, and of entries naming the same folder only the first is kept. The survivors
    /// are the instances passed in, in the order they came.
    /// </summary>
    public static IReadOnlyList<FolderPath> Minimal(IEnumerable<FolderPath> folders)
    {
        var kept = new List<FolderPath>();
        foreach (var folder in folders)
        {
            // The same folder as one already kept, or a folder inside one.
            if (kept.Any(k => k.Contains(folder)))
                continue;

            // Kept entries inside this one give way to it. None is the same folder, or it would have matched above.
            kept.RemoveAll(folder.Contains);
            kept.Add(folder);
        }

        return kept;
    }

    /// <summary>One spelling of the path: backslashes, and no trailing separator but a drive root's own (<c>D:\</c>).</summary>
    public override string ToString() => Path.Join(Root, string.Join(Path.DirectorySeparatorChar, Names));
}
