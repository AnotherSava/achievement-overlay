using System.IO;
using System.Text;

namespace AchievementOverlay.GbeConfig;

/// <summary>
/// Result of a DRM heuristic scan of a game directory.
/// </summary>
public sealed class DrmReport
{
    public long LargestExeSizeBytes { get; init; }
    public string? LargestExePath { get; init; }
    public bool DenuvoStringFound { get; init; }
    public IReadOnlyList<string> CrackIndicators { get; init; } = Array.Empty<string>();

    /// <summary>An exe larger than the size threshold — modern Denuvo bloats the binary.</summary>
    public bool SizeSuspicious => LargestExeSizeBytes > DrmDetector.SuspiciousExeSizeBytes;

    /// <summary>Denuvo is likely present (confirmed by string, or strongly suspected by size).</summary>
    public bool DenuvoLikely => DenuvoStringFound || SizeSuspicious;

    public bool HasCrackIndicator => CrackIndicators.Count > 0;
}

/// <summary>
/// Heuristic Denuvo / crack detection mirroring the track-achievements skill: exe
/// size, an embedded <c>Denuvo</c> string, and adjacent crack-distribution markers.
/// </summary>
public static class DrmDetector
{
    public const long SuspiciousExeSizeBytes = 200L * 1024 * 1024;

    private static readonly string[] CrackExtensions = { ".rzr", ".rne", ".cdx", ".bak" };
    private static readonly byte[] DenuvoMarker = Encoding.ASCII.GetBytes("Denuvo");

    public static DrmReport Analyze(string gameDir)
    {
        if (!Directory.Exists(gameDir))
            return new DrmReport();

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };

        long largestSize = 0;
        string? largestPath = null;
        var denuvoFound = false;

        foreach (var exe in Directory.EnumerateFiles(gameDir, "*.exe", options))
        {
            long size;
            try
            {
                size = new FileInfo(exe).Length;
            }
            catch (IOException)
            {
                continue;
            }

            if (size > largestSize)
            {
                largestSize = size;
                largestPath = exe;
            }

            if (!denuvoFound && FileContainsBytes(exe, DenuvoMarker))
                denuvoFound = true;
        }

        var crackIndicators = new List<string>();
        foreach (var file in Directory.EnumerateFiles(gameDir, "*.*", options))
        {
            if (CrackExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                crackIndicators.Add(file);
        }

        return new DrmReport
        {
            LargestExeSizeBytes = largestSize,
            LargestExePath = largestPath,
            DenuvoStringFound = denuvoFound,
            CrackIndicators = crackIndicators
        };
    }

    /// <summary>Streams a file looking for a byte pattern, tolerant of buffer boundaries.</summary>
    private static bool FileContainsBytes(string path, byte[] pattern)
    {
        if (pattern.Length == 0)
            return false;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            const int chunkSize = 1 << 20; // 1 MiB
            var overlap = pattern.Length - 1;
            var buffer = new byte[chunkSize + overlap];
            var carried = 0;
            int read;
            while ((read = stream.Read(buffer, carried, chunkSize)) > 0)
            {
                var available = carried + read;
                if (IndexOf(buffer, available, pattern) >= 0)
                    return true;

                // Carry the trailing bytes so a pattern split across chunks is still found.
                carried = Math.Min(overlap, available);
                Array.Copy(buffer, available - carried, buffer, 0, carried);
            }
        }
        catch (IOException)
        {
            return false;
        }
        return false;
    }

    private static int IndexOf(byte[] haystack, int length, byte[] needle)
    {
        var limit = length - needle.Length;
        for (var i = 0; i <= limit; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
                return i;
        }
        return -1;
    }
}
