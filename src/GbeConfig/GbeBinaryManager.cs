using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using SharpCompress.Archives.SevenZip;
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

    /// <summary>
    /// The Windows release archive, in order of preference. GBE renamed its VS 2026 build from the
    /// plain name to <c>-vs26</c> (gbe_fork issue #605); <c>-vs22</c> beside it is a Windows 7
    /// compatibility build this app has no use for, since it needs Windows 10 itself.
    /// </summary>
    private static readonly string[] ReleaseAssetNames = ["emu-win-release-vs26.7z", "emu-win-release.7z"];

    /// <summary>The debug build's archive, named the same way. It extracts under <c>debug/</c>, beside the release build's <c>release/</c>.</summary>
    private static readonly string[] DebugAssetNames = ["emu-win-debug-vs26.7z", "emu-win-debug.7z"];

    /// <summary>The folder a debug build's binaries sit under, which is how they are told from the release build's.</summary>
    private const string DebugFolder = "/debug/";

    public static string CacheRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AchievementOverlay", "gbe-cache");

    /// <summary>
    /// Resolves GBE binaries in <paramref name="folder"/>. When <paramref name="downloadLatest"/>
    /// is false, only what's already in the folder is used (no network). Otherwise the latest
    /// release is downloaded into the folder (under a tag subfolder) and reused if already present.
    /// <paramref name="debug"/> asks for GBE's debug build, which logs every Steam call the game makes.
    /// Throws if the required binaries can't be found/obtained.
    /// </summary>
    public static async Task<GbeRelease> EnsureAsync(
        string folder, bool downloadLatest, bool debug, HttpClient http, IConfigProgress log, CancellationToken ct = default)
    {
        var build = debug ? "debug" : "release";
        if (string.IsNullOrWhiteSpace(folder))
            folder = CacheRoot;

        if (!downloadLatest)
        {
            var located = LocateBinaries(folder, "(local)", debug) ?? throw new FileNotFoundException($"No GBE {build} build found in '{folder}'. Enable 'Download the latest GBE release', or point at a folder containing one.");
            log.Report(ConfigLogLevel.Info, $"Using the existing GBE {build} build in '{folder}'.");
            return located;
        }

        log.Report(ConfigLogLevel.Step, "Resolving latest GBE release...");
        var (tag, assetName, downloadUrl) = await GetLatestReleaseAsync(http, debug, ct);
        log.Report(ConfigLogLevel.Debug, $"Latest GBE tag: {tag}");

        var tagDir = Path.Combine(folder, tag);
        var cached = LocateBinaries(tagDir, tag, debug);
        if (cached != null)
        {
            log.Report(ConfigLogLevel.Info, $"Using the cached GBE {build} build of {tag}.");
            return cached;
        }

        log.Report(ConfigLogLevel.Step, $"Downloading GBE release {tag} ({assetName})...");
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

        return LocateBinaries(tagDir, tag, debug)
            ?? throw new FileNotFoundException($"GBE {build} build of {tag} extracted but expected binaries were not found in '{tagDir}'");
    }

    /// <summary>
    /// Finds the regular x64 DLL of the release or the debug build, and the interface generator, under a
    /// directory tree. Returns null if either is missing. A cache folder holds both builds once both have
    /// been asked for, so the DLL is chosen by whether it sits under <c>debug/</c>.
    /// </summary>
    public static GbeRelease? LocateBinaries(string rootDir, string version, bool debug = false)
    {
        if (!Directory.Exists(rootDir))
            return null;

        var options = AppUtilities.RecursiveScan;

        var regularDll = Directory.EnumerateFiles(rootDir, "steam_api64.dll", options)
            .FirstOrDefault(p => Below(p).Contains("/regular/", StringComparison.OrdinalIgnoreCase) && Below(p).Contains(DebugFolder, StringComparison.OrdinalIgnoreCase) == debug);

        var generator = Directory.EnumerateFiles(rootDir, "generate_interfaces_x64.exe", options).FirstOrDefault();

        if (regularDll == null || generator == null)
            return null;

        return new GbeRelease(regularDll, generator, version);

        // Only the part below rootDir is a build's layout; the folder the user chose may be named anything.
        string Below(string path) => "/" + Path.GetRelativePath(rootDir, path).Replace('\\', '/');
    }

    private static async Task<(string Tag, string AssetName, string DownloadUrl)> GetLatestReleaseAsync(HttpClient http, bool debug, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.UserAgent.ParseAdd("AchievementOverlay");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseLatestRelease(json, debug);
    }

    public static (string Tag, string AssetName, string DownloadUrl) ParseLatestRelease(string json, bool debug = false)
    {
        var assetNames = debug ? DebugAssetNames : ReleaseAssetNames;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "latest" : "latest";

        var urls = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out var n) && n.GetString() is { } name && asset.TryGetProperty("browser_download_url", out var u))
                    urls[name] = u.GetString();
            }
        }

        foreach (var assetName in assetNames)
        {
            if (urls.TryGetValue(assetName, out var url))
                return (tag, assetName, url ?? throw new InvalidOperationException("Release asset has no download URL"));
        }

        throw new InvalidOperationException($"Latest GBE release has none of the expected assets ({string.Join(", ", assetNames)})");
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

        var destFolder = FolderPath.Parse(destination);
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
            var safe = destFolder.Contains(FolderPath.Parse(outPath));
            if (safe)
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

            using var entryStream = reader.OpenEntryStream();
            using var outStream = safe ? new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None) : Stream.Null;
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

    private static string Mb(long bytes) => (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.CurrentCulture);
}
