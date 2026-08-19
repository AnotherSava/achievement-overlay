using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;

namespace AchievementOverlay;

/// <summary>
/// Loads a TrueType file from disk as a usable <see cref="FontFamily"/>.
/// </summary>
/// <remarks>
/// This exists because the obvious line does not work and does not say so:
/// <c>new FontFamily(@"C:\…\poppins.ttf")</c> compiles, throws nothing, and draws whatever WPF falls
/// back to — measured on a real file, Arial, which is neither the requested font nor the configured
/// one. WPF treats the argument as a family <em>name</em> and never looks at the disk. A file has to
/// be addressed as a base URI plus the family name stored inside it, which only
/// <see cref="Fonts.GetFontFamilies(Uri, string)"/> can supply.
/// </remarks>
internal static class PopupFontLoader
{
    /// <summary>
    /// Misses are cached as well as hits: a font WPF cannot parse costs a disk read to discover, and
    /// this runs on the dispatcher just before a popup is drawn.
    /// </summary>
    private static readonly ConcurrentDictionary<string, FontFamily?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The family in the given font file, or null when there is no path, the file is unreachable, or
    /// WPF finds no family in it — a <c>.ttc</c> or a corrupt file yields an empty sequence rather
    /// than an exception, so "no families" is a normal result and not an error.
    /// </summary>
    public static FontFamily? Load(string? fontFilePath) =>
        string.IsNullOrWhiteSpace(fontFilePath) ? null : Cache.GetOrAdd(fontFilePath, LoadUncached);

    private static FontFamily? LoadUncached(string fontFilePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(fontFilePath));
            if (string.IsNullOrEmpty(directory))
                return null;

            // Enumerating a network location blocks the dispatcher for as long as the share takes to
            // answer, and a popup is not worth that.
            var baseUri = new Uri(directory + Path.DirectorySeparatorChar);
            if (baseUri.IsUnc)
            {
                Logger.Warn($"Ignoring font on a network path: '{fontFilePath}'");
                return null;
            }

            // Addressed by file name, not by folder: the folder overload enumerates every font beside
            // it, and GBE's own fonts folder ships more than one file.
            var family = Fonts.GetFontFamilies(baseUri, Path.GetFileName(fontFilePath)).FirstOrDefault();
            if (family == null)
                Logger.Warn($"No usable font family in '{fontFilePath}'; using the configured family instead.");
            return family;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not load font '{fontFilePath}': {ex.Message}");
            return null;
        }
    }
}
