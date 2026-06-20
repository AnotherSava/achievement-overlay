using System.IO;
using AchievementOverlay.GbeConfig;
using Xunit;

namespace AchievementOverlay.Tests.GbeConfig;

public class GamesPathPlannerTests
{
    [Fact]
    public void PlanRootToAdd_GameUnderExistingRoot_ReturnsNull()
    {
        var result = GamesPathPlanner.PlanRootToAdd(new[] { @"C:\Games" }, @"C:\Games\Frostpunk 2");
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
}
