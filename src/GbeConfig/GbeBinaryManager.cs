using System.IO;
using System.Net.Http;
using System.Text.Json;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace AchievementOverlay.GbeConfig;

/// <summary>Located GBE binaries needed to configure a game.</summary>
public sealed record GbeRelease(string RegularDll64, string GenerateInterfacesExe64, string Version);

/// <summary>
/// Locates GBE release binaries — the regular x64 <c>steam_api64.dll</c> and
/// <c>generate_interfaces_x64.exe</c>. Uses a caller-supplied directory if given,
/// otherwise downloads the latest GBE release and caches it under
/// <c>%LOCALAPPDATA%/AchievementOverlay/gbe-cache/&lt;tag&gt;/</c>.
/// </summary>
public static class GbeBinaryManager
{
    private const string LatestReleaseApi = "https://api.github.com/repos/Detanup01/gbe_fork/releases/latest";
    private const string AssetName = "emu-win-release.7z";

    public static string CacheRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AchievementOverlay", "gbe-cache");

    /// <summary>
    /// Resolves GBE binaries in <paramref name="folder"/>. When <paramref name="downloadLatest"/>
    /// is false, only what's already in the folder is used (no network). Otherwise the latest
    /// release is downloaded into the folder (under a tag subfolder) and reused if already present.
    /// Throws if the required binaries can't be found/obtained.
    /// </summary>
    public static async Task<GbeRelease> EnsureAsync(
        string folder, bool downloadLatest, HttpClient http, IConfigProgress log, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(folder))
            folder = CacheRoot;

        if (!downloadLatest)
        {
            var located = LocateBinaries(folder, "(local)");
            if (located == null)
                throw new FileNotFoundException(
                    $"No GBE release found in '{folder}'. Enable 'Download the latest GBE release', or point at a folder containing one.");
            log.Report(ConfigLogLevel.Info, $"Using existing GBE release in '{folder}'.");
            return located;
        }

        log.Report(ConfigLogLevel.Step, "Resolving latest GBE release...");
        var (tag, downloadUrl) = await GetLatestReleaseAsync(http, ct);
        log.Report(ConfigLogLevel.Debug, $"Latest GBE tag: {tag}");

        var tagDir = Path.Combine(folder, tag);
        var cached = LocateBinaries(tagDir, tag);
        if (cached != null)
        {
            log.Report(ConfigLogLevel.Info, $"Using cached GBE release {tag}.");
            return cached;
        }

        log.Report(ConfigLogLevel.Step, $"Downloading GBE release {tag} ({AssetName})...");
        // Download the archive INTO the GBE folder (not %TEMP%) so a single Defender exclusion on
        // this folder covers both the .7z and the extracted DLLs — otherwise extraction is blocked.
        Directory.CreateDirectory(folder);
        var archivePath = Path.Combine(folder, $"ao-gbe-{tag}-{Guid.NewGuid():N}.7z");
        try
        {
            await DownloadAsync(downloadUrl, archivePath, http, log, ct);
            log.Report(ConfigLogLevel.Step, "Extracting...");
            Directory.CreateDirectory(tagDir);
            Extract7z(archivePath, tagDir, log, ct);
        }
        finally
        {
            try { File.Delete(archivePath); } catch (IOException) { /* best-effort */ }
        }

        return LocateBinaries(tagDir, tag)
            ?? throw new FileNotFoundException($"GBE release {tag} extracted but expected binaries were not found in '{tagDir}'");
    }

    /// <summary>
    /// Finds the regular x64 DLL and the interface generator under a directory tree.
    /// Returns null if either is missing.
    /// </summary>
    public static GbeRelease? LocateBinaries(string rootDir, string version)
    {
        if (!Directory.Exists(rootDir))
            return null;

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };

        var regularDll = Directory.EnumerateFiles(rootDir, "steam_api64.dll", options)
            .FirstOrDefault(p => p.Replace('\\', '/').Contains("/regular/", StringComparison.OrdinalIgnoreCase));

        var generator = Directory.EnumerateFiles(rootDir, "generate_interfaces_x64.exe", options).FirstOrDefault();

        if (regularDll == null || generator == null)
            return null;

        return new GbeRelease(regularDll, generator, version);
    }

    private static async Task<(string Tag, string DownloadUrl)> GetLatestReleaseAsync(HttpClient http, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.UserAgent.ParseAdd("AchievementOverlay");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseLatestRelease(json);
    }

    public static (string Tag, string DownloadUrl) ParseLatestRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "latest" : "latest";

        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.Equals(name, AssetName, StringComparison.OrdinalIgnoreCase)
                    && asset.TryGetProperty("browser_download_url", out var u))
                {
                    return (tag, u.GetString() ?? throw new InvalidOperationException("Release asset has no download URL"));
                }
            }
        }

        throw new InvalidOperationException($"Latest GBE release has no '{AssetName}' asset");
    }

    private static async Task DownloadAsync(string url, string destination, HttpClient http, IConfigProgress log, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0;
        await using var src = await response.Content.ReadAsStreamAsync(ct);
        await using var dst = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long done = 0;
        var lastPct = -1;
        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            if (total > 0)
            {
                var pct = (int)(done * 100 / total);
                if (pct != lastPct)
                {
                    lastPct = pct;
                    log.UpdateStep($"Downloading GBE release… {pct}% ({Mb(done)} / {Mb(total)} MB)");
                }
            }
            else
            {
                log.UpdateStep($"Downloading GBE release… {Mb(done)} MB");
            }
        }
    }

    /// <summary>Sequential (reader-based) extraction — correct/fast for solid 7z archives — with
    /// byte-level progress and responsive cancellation (checked mid-file).</summary>
    private static void Extract7z(string archivePath, string destination, IConfigProgress log, CancellationToken ct)
    {
        using var archive = SevenZipArchive.OpenArchive(new FileInfo(archivePath), new ReaderOptions());
        var total = archive.TotalUncompressedSize;
        long done = 0;
        var lastPct = -1;

        var destFull = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using var reader = archive.ExtractAllEntries();
        var buffer = new byte[81920];
        while (reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested();
            var entry = reader.Entry;
            if (entry.IsDirectory || string.IsNullOrEmpty(entry.Key))
                continue;

            // Always drain the entry stream (write to file, or to Null if the path is unsafe) so the
            // sequential reader stays in sync on solid archives.
            var outPath = Path.GetFullPath(Path.Combine(destination, entry.Key.Replace('/', Path.DirectorySeparatorChar)));
            var safe = outPath.StartsWith(destFull, StringComparison.OrdinalIgnoreCase);
            if (safe)
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

            using var entryStream = reader.OpenEntryStream();
            using Stream outStream = safe
                ? new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None)
                : Stream.Null;
            int read;
            while ((read = entryStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested(); // cancel responsively, even mid-file
                outStream.Write(buffer, 0, read);
                done += read;
                if (total > 0)
                {
                    var pct = (int)(done * 100 / total);
                    if (pct != lastPct)
                    {
                        lastPct = pct;
                        log.UpdateStep($"Extracting… {pct}%");
                    }
                }
            }
        }
    }

    private static string Mb(long bytes) => (bytes / 1024.0 / 1024.0).ToString("0.0");
}
