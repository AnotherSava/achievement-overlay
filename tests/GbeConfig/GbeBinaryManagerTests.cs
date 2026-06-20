using System.IO;
using AchievementOverlay.GbeConfig;
using Xunit;

namespace AchievementOverlay.Tests.GbeConfig;

public class GbeBinaryManagerTests : IDisposable
{
    private readonly string _tempDir;

    public GbeBinaryManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GbeBinaryManagerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private void Touch(string relativePath)
    {
        var full = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "");
    }

    [Fact]
    public void ParseLatestRelease_ExtractsTagAndAssetUrl()
    {
        var json = """
        {
          "tag_name": "release-1.2.3",
          "assets": [
            {"name": "emu-linux-release.tar.gz", "browser_download_url": "https://x/linux"},
            {"name": "emu-win-release.7z", "browser_download_url": "https://x/win.7z"}
          ]
        }
        """;
        var (tag, url) = GbeBinaryManager.ParseLatestRelease(json);
        Assert.Equal("release-1.2.3", tag);
        Assert.Equal("https://x/win.7z", url);
    }

    [Fact]
    public void ParseLatestRelease_MissingAsset_Throws()
    {
        var json = """{"tag_name": "v1", "assets": [{"name": "other.zip", "browser_download_url": "u"}]}""";
        Assert.Throws<InvalidOperationException>(() => GbeBinaryManager.ParseLatestRelease(json));
    }

    [Fact]
    public void LocateBinaries_FindsRegularDllAndGenerator()
    {
        Touch(@"release\regular\x64\steam_api64.dll");
        Touch(@"release\experimental\x64\steam_api64.dll");
        Touch(@"release\tools\generate_interfaces\generate_interfaces_x64.exe");

        var located = GbeBinaryManager.LocateBinaries(_tempDir, "v1");
        Assert.NotNull(located);
        Assert.Contains(Path.Combine("regular", "x64"), located!.RegularDll64);
        Assert.EndsWith("generate_interfaces_x64.exe", located.GenerateInterfacesExe64);
        Assert.Equal("v1", located.Version);
    }

    [Fact]
    public void LocateBinaries_PrefersRegularOverExperimental()
    {
        Touch(@"release\experimental\x64\steam_api64.dll");
        Touch(@"release\tools\generate_interfaces\generate_interfaces_x64.exe");

        // Only experimental present → no "regular" DLL → cannot locate.
        Assert.Null(GbeBinaryManager.LocateBinaries(_tempDir, "v1"));
    }

    [Fact]
    public void LocateBinaries_MissingGenerator_ReturnsNull()
    {
        Touch(@"release\regular\x64\steam_api64.dll");
        Assert.Null(GbeBinaryManager.LocateBinaries(_tempDir, "v1"));
    }

    [Fact]
    public void LocateBinaries_NonexistentDir_ReturnsNull()
    {
        Assert.Null(GbeBinaryManager.LocateBinaries(Path.Combine(_tempDir, "nope"), "v1"));
    }
}
