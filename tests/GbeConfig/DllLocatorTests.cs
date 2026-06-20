using System.IO;
using AchievementOverlay.GbeConfig;
using Xunit;

namespace AchievementOverlay.Tests.GbeConfig;

public class DllLocatorTests : IDisposable
{
    private readonly string _tempDir;

    public DllLocatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DllLocatorTests_" + Guid.NewGuid().ToString("N"));
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
    public void FindAll_NoDlls_ReturnsEmpty()
    {
        Assert.Empty(DllLocator.FindAll(_tempDir));
    }

    [Fact]
    public void FindAll_FindsNestedDll()
    {
        Touch(@"Engine\Binaries\ThirdParty\Steamworks\Steamv157\Win64\steam_api64.dll");
        var found = DllLocator.FindAll(_tempDir);
        Assert.Single(found);
        Assert.True(found[0].Is64);
    }

    [Fact]
    public void FindAll_Prefers64BitFirst()
    {
        Touch(@"a\steam_api.dll");
        Touch(@"b\steam_api64.dll");
        var found = DllLocator.FindAll(_tempDir);
        Assert.Equal(2, found.Count);
        Assert.True(found[0].Is64);
        Assert.False(found[1].Is64);
    }

    [Fact]
    public void FindAll_SamearchPrefersShallowest()
    {
        Touch(@"deep\nested\path\steam_api64.dll");
        Touch(@"shallow\steam_api64.dll");
        var found = DllLocator.FindAll(_tempDir);
        Assert.Equal(2, found.Count);
        Assert.Contains("shallow", found[0].Path);
    }

    [Fact]
    public void SelectPrimary_PicksFirst()
    {
        Touch(@"a\steam_api.dll");
        Touch(@"b\steam_api64.dll");
        var primary = DllLocator.SelectPrimary(DllLocator.FindAll(_tempDir));
        Assert.NotNull(primary);
        Assert.True(primary!.Is64);
    }
}
