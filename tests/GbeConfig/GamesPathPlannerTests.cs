using System.IO;
using AchievementOverlay.GbeConfig;
using Xunit;

namespace AchievementOverlay.Tests.GbeConfig;

public class GamesPathPlannerTests
{
    [Fact]
    public void PlanRootToAdd_GameUnderExistingRoot_ReturnsNull()
    {
        var result = GamesPathPlanner.PlanRootToAdd(new[] { @"C:\Games" }, @"C:\Games\Example Game");
        Assert.Null(result);
    }

    [Fact]
    public void PlanRootToAdd_GameDeepUnderExistingRoot_ReturnsNull()
    {
        var result = GamesPathPlanner.PlanRootToAdd(new[] { @"C:\Games" }, @"C:\Games\Foo\Engine\Win64");
        Assert.Null(result);
    }

    [Fact]
    public void PlanRootToAdd_GameEqualsExistingRoot_ReturnsNull()
    {
        var result = GamesPathPlanner.PlanRootToAdd(new[] { @"C:\Games\Foo" }, @"C:\Games\Foo");
        Assert.Null(result);
    }

    [Fact]
    public void PlanRootToAdd_CaseInsensitiveMatch_ReturnsNull()
    {
        var result = GamesPathPlanner.PlanRootToAdd(new[] { @"C:\GAMES" }, @"C:\games\Foo");
        Assert.Null(result);
    }

    [Fact]
    public void PlanRootToAdd_SimilarPrefixButNotAncestor_AddsParent()
    {
        // C:\GamesOther is not an ancestor of C:\Games\Foo despite the shared prefix.
        var result = GamesPathPlanner.PlanRootToAdd(new[] { @"C:\GamesOther" }, @"C:\Games\Foo");
        Assert.Equal(@"C:\Games", result);
    }

    [Fact]
    public void PlanRootToAdd_NotCovered_ReturnsParentFolder()
    {
        var result = GamesPathPlanner.PlanRootToAdd(new[] { @"C:\Games" }, @"D:\Stuff\MyGame");
        Assert.Equal(@"D:\Stuff", result);
    }

    [Fact]
    public void PlanRootToAdd_TrailingSeparatorOnRoot_StillCovers()
    {
        var result = GamesPathPlanner.PlanRootToAdd(new[] { @"C:\Games\" }, @"C:\Games\Foo");
        Assert.Null(result);
    }

    [Theory]
    [InlineData("C:/Games/")]
    [InlineData("C:/Games")]
    [InlineData(@"c:\games\\")]
    public void PlanRootToAdd_RootInAnotherSpelling_StillCovers(string existing) => Assert.Null(GamesPathPlanner.PlanRootToAdd(new[] { existing }, @"C:\Games\Foo"));

    [Fact]
    public void PlanRootToAdd_DriveRootCoversAGameOnIt_ReturnsNull()
    {
        // A drive root already ends in its separator ('D:\'). A prefix test that appends another one
        // matches no game on the drive, and each Add game there then writes 'D:\' into config again.
        var result = GamesPathPlanner.PlanRootToAdd(new[] { @"D:\" }, @"D:\Example Game");
        Assert.Null(result);
    }

    [Fact]
    public void PlanRootToAdd_EmptyExistingPaths_AddsParent()
    {
        var result = GamesPathPlanner.PlanRootToAdd(Array.Empty<string>(), @"D:\Stuff\MyGame");
        Assert.Equal(@"D:\Stuff", result);
    }

    [Fact]
    public void PlanRootToAdd_GameAtDriveRoot_ReturnsGameItself()
    {
        var result = GamesPathPlanner.PlanRootToAdd(new[] { @"C:\Games" }, @"D:\");
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(@"D:\")), result);
    }

    [Fact]
    public void GamesRootOf_GameUnderNestedRoots_ReturnsDeepest() => Assert.Equal(@"D:\Games", GamesPathPlanner.GamesRootOf(new[] { @"D:\", @"D:\Games" }, @"D:\Games\Example Game\bin"));

    [Fact]
    public void GamesRootOf_GameOutsideEveryRoot_ReturnsNull() => Assert.Null(GamesPathPlanner.GamesRootOf(new[] { @"C:\Games" }, @"E:\Example Game-REPACK\Example Game"));

    [Fact]
    public void ChooseBrowseStart_SingleRoot_ReturnsItWhateverWasLast()
    {
        Assert.Equal(@"C:\Games", GamesPathPlanner.ChooseBrowseStart(new[] { @"C:\Games" }, null));
        Assert.Equal(@"C:\Games", GamesPathPlanner.ChooseBrowseStart(new[] { @"C:\Games" }, @"E:\Other"));
    }

    [Fact]
    public void ChooseBrowseStart_SeveralRoots_ReturnsTheOneLastUsed() => Assert.Equal(@"D:\Games", GamesPathPlanner.ChooseBrowseStart(new[] { @"C:\Games", @"D:\Games" }, @"d:\games"));

    [Fact]
    public void ChooseBrowseStart_SeveralRootsLastUsedNested_ReturnsDeepestContaining() => Assert.Equal(@"D:\Games", GamesPathPlanner.ChooseBrowseStart(new[] { @"D:\", @"D:\Games" }, @"D:\Games"));

    [Fact]
    public void ChooseBrowseStart_SeveralRootsNoHistory_ReturnsNull() => Assert.Null(GamesPathPlanner.ChooseBrowseStart(new[] { @"C:\Games", @"D:\Games" }, null));

    [Fact]
    public void ChooseBrowseStart_SeveralRootsLastUsedNoLongerListed_ReturnsNull() => Assert.Null(GamesPathPlanner.ChooseBrowseStart(new[] { @"C:\Games", @"D:\Games" }, @"E:\Other"));
}
