using System.IO;
using System.Text;
using AchievementOverlay.GbeConfig;
using Xunit;

namespace AchievementOverlay.Tests.GbeConfig;

public class DrmDetectorTests : IDisposable
{
    private readonly string _tempDir;

    public DrmDetectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DrmDetectorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Analyze_CleanGame_NoDrm()
    {
        File.WriteAllText(Path.Combine(_tempDir, "game.exe"), "clean small exe");
        var report = DrmDetector.Analyze(_tempDir);

        Assert.False(report.DenuvoStringFound);
        Assert.False(report.SizeSuspicious);
        Assert.False(report.DenuvoLikely);
        Assert.False(report.HasCrackIndicator);
    }

    [Fact]
    public void Analyze_DenuvoString_IsDetected()
    {
        File.WriteAllText(Path.Combine(_tempDir, "game.exe"), "lots of bytes Denuvo Anti-Tamper here");
        var report = DrmDetector.Analyze(_tempDir);

        Assert.True(report.DenuvoStringFound);
        Assert.True(report.DenuvoLikely);
    }

    [Fact]
    public void Analyze_DenuvoStringSplitAcrossChunkBoundary_IsDetected()
    {
        // Place "Denuvo" straddling a 1 MiB chunk boundary to exercise the carry logic.
        var path = Path.Combine(_tempDir, "big.exe");
        const int chunk = 1 << 20;
        var marker = Encoding.ASCII.GetBytes("Denuvo");
        var buffer = new byte[chunk + marker.Length];
        // Put marker starting 3 bytes before the boundary.
        Array.Copy(marker, 0, buffer, chunk - 3, marker.Length);
        File.WriteAllBytes(path, buffer);

        Assert.True(DrmDetector.Analyze(_tempDir).DenuvoStringFound);
    }

    [Fact]
    public void Analyze_CrackIndicators_AreCollected()
    {
        File.WriteAllText(Path.Combine(_tempDir, "game.exe"), "x");
        File.WriteAllText(Path.Combine(_tempDir, "steam_api64.dll.rzr"), "x");
        File.WriteAllText(Path.Combine(_tempDir, "patch.cdx"), "x");
        var report = DrmDetector.Analyze(_tempDir);

        Assert.True(report.HasCrackIndicator);
        Assert.Equal(2, report.CrackIndicators.Count);
    }

    [Fact]
    public void Analyze_TracksLargestExe()
    {
        File.WriteAllText(Path.Combine(_tempDir, "small.exe"), "abc");
        File.WriteAllText(Path.Combine(_tempDir, "large.exe"), new string('y', 5000));
        var report = DrmDetector.Analyze(_tempDir);

        Assert.Equal("large.exe", Path.GetFileName(report.LargestExePath));
        Assert.Equal(5000, report.LargestExeSizeBytes);
    }
}
