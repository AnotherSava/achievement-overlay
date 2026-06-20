using System.IO;
using System.Net.Http;

namespace AchievementOverlay.GbeConfig;

/// <summary>Summary of an icon-download pass.</summary>
public sealed record IconDownloadResult(int Downloaded, int Skipped, int Failed);

/// <summary>
/// Downloads achievement icons (unlocked + locked) into the
/// <c>achievement_images/</c> folder, in parallel. Filenames match the paths written
/// by <see cref="AchievementSchemaWriter"/>: <c>&lt;name&gt;.jpg</c> / <c>&lt;name&gt;_gray.jpg</c>.
/// </summary>
public static class IconDownloader
{
    public static async Task<IconDownloadResult> DownloadAllAsync(
        IEnumerable<SteamSchemaAchievement> schema, string imageDir, HttpClient http,
        bool overwrite = false, CancellationToken ct = default)
    {
        Directory.CreateDirectory(imageDir);

        var jobs = new List<(string Url, string Path)>();
        foreach (var a in schema)
        {
            if (!string.IsNullOrEmpty(a.Icon))
                jobs.Add((a.Icon, Path.Combine(imageDir, $"{a.Name}.jpg")));
            if (!string.IsNullOrEmpty(a.IconGray))
                jobs.Add((a.IconGray, Path.Combine(imageDir, $"{a.Name}_gray.jpg")));
        }

        var downloaded = 0;
        var skipped = 0;
        var failed = 0;

        using var throttle = new SemaphoreSlim(8);
        var tasks = jobs.Select(async job =>
        {
            if (!overwrite && File.Exists(job.Path))
            {
                Interlocked.Increment(ref skipped);
                return;
            }

            await throttle.WaitAsync(ct);
            try
            {
                var bytes = await http.GetByteArrayAsync(job.Url, ct);
                await File.WriteAllBytesAsync(job.Path, bytes, ct);
                Interlocked.Increment(ref downloaded);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                Interlocked.Increment(ref failed);
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(tasks);
        return new IconDownloadResult(downloaded, skipped, failed);
    }
}
