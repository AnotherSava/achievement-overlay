using System.Collections.Concurrent;
using System.IO;

namespace AchievementOverlay;

/// <summary>
/// The app's embedded resources: opened as streams, or extracted to temp files for the WPF
/// notification window, which loads icons from file paths rather than streams. Extraction is
/// cached per resource.
/// </summary>
public static class EmbeddedAssets
{
    private const string TrackingConfiguredResource = "AchievementOverlay.tracking_configured.jpg";
    private const string ConnoisseurResource = "AchievementOverlay.connoisseur.jpg";

    private static readonly ConcurrentDictionary<string, string> _extracted = new();

    /// <summary>Icon for the synthetic "Achievement tracking configured" notification.</summary>
    public static string? GetTrackingConfiguredIconPath()
        => ExtractToTemp(TrackingConfiguredResource, "AchievementOverlay_tracking_configured.jpg");

    /// <summary>Icon for the synthetic "Achievement Connoisseur" recent-achievements entry.</summary>
    public static string? GetConnoisseurIconPath()
        => ExtractToTemp(ConnoisseurResource, "AchievementOverlay_connoisseur.jpg");

    /// <summary>
    /// Opens a resource the project embeds unconditionally. One missing means the build is broken, so
    /// this throws rather than leave a caller to substitute something for it.
    /// </summary>
    public static Stream Open(string resourceName)
        => typeof(EmbeddedAssets).Assembly.GetManifestResourceStream(resourceName) ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' is missing, so this build is broken: the project embeds it unconditionally");

    /// <summary>
    /// Extracts an embedded resource to a temp file (extract-once + cache). Returns the temp
    /// path, or null if the resource is not present in the assembly.
    /// </summary>
    public static string? ExtractToTemp(string resourceName, string tempFileName)
    {
        if (_extracted.TryGetValue(resourceName, out var cached) && File.Exists(cached))
            return cached;

        var stream = typeof(EmbeddedAssets).Assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return null;

        var tempPath = Path.Combine(Path.GetTempPath(), tempFileName);
        using (stream)
        using (var file = File.Create(tempPath))
            stream.CopyTo(file);

        _extracted[resourceName] = tempPath;
        return tempPath;
    }
}
