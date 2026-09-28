using Xunit;

namespace AchievementOverlay.Tests;

public class FolderPathTests
{
    // --- Parse ---

    [Fact]
    public void Parse_ForwardSlashes_SeparateNames()
    {
        var folder = FolderPath.Parse("D:/Games/Aphelion");

        Assert.Equal(new[] { "Games", "Aphelion" }, folder.Names);
        Assert.Equal(@"D:\Games\Aphelion", folder.ToString());
    }

    [Theory]
    [InlineData(@"D:\Games\")]
    [InlineData(@"D:\Games\\")]
    [InlineData(@"D:\\Games")]
    public void Parse_TrailingOrDoubledSeparator_LeavesNoEmptyName(string path) => Assert.Equal(new[] { "Games" }, FolderPath.Parse(path).Names);

    [Fact]
    public void Parse_DriveRoot_HasNoNamesAndKeepsItsSeparator()
    {
        var root = FolderPath.Parse(@"D:\");

        Assert.Empty(root.Names);
        Assert.Equal(@"D:\", root.ToString());
    }

    [Fact]
    public void Parse_KeepsCaseAsWritten() => Assert.Equal(@"d:\GAMES\Aphelion", FolderPath.Parse(@"d:\GAMES\Aphelion").ToString());

    [Fact]
    public void Parse_DotDot_IsResolved() => Assert.Equal(new[] { "Other" }, FolderPath.Parse(@"D:\Games\..\Other").Names);

    [Fact]
    public void Parse_NetworkShare_RootIsTheShare()
    {
        var folder = FolderPath.Parse(@"\\server\share\Games\");

        Assert.Equal(new[] { "Games" }, folder.Names);
        Assert.Equal(@"\\server\share\Games", folder.ToString());
        Assert.Equal(@"\\server\share", FolderPath.Parse(@"\\server\share\").ToString());
    }

    // --- Contains ---

    [Fact]
    public void Contains_DriveRoot_HoldsAFolderOnIt() => Assert.True(FolderPath.Parse(@"D:\").Contains(FolderPath.Parse(@"D:\X")));

    [Theory]
    [InlineData(@"C:\Games")]
    [InlineData(@"c:\GAMES")]
    [InlineData(@"C:/Games/")]
    public void Contains_TheFolderItself_InAnySpelling(string spelling) => Assert.True(FolderPath.Parse(@"C:\Games").Contains(FolderPath.Parse(spelling)));

    [Fact]
    public void Contains_DeepFolder_IgnoringCase() => Assert.True(FolderPath.Parse(@"C:\GAMES").Contains(FolderPath.Parse(@"c:\games\Aphelion\Engine\Win64")));

    [Fact]
    public void Contains_SiblingSharingTheName_IsNotInside() => Assert.False(FolderPath.Parse(@"C:\Games").Contains(FolderPath.Parse(@"C:\GamesOther")));

    [Fact]
    public void Contains_SameFoldersOnAnotherDrive_IsNotInside() => Assert.False(FolderPath.Parse(@"C:\Games").Contains(FolderPath.Parse(@"D:\Games\X")));

    [Fact]
    public void Contains_ParentOfTheFolder_IsNotInside() => Assert.False(FolderPath.Parse(@"C:\Games\X").Contains(FolderPath.Parse(@"C:\Games")));

    // --- FirstNameBelow ---

    [Fact]
    public void FirstNameBelow_DeepFolder_IsItsFirstFolderBelow() => Assert.Equal("Aphelion", FolderPath.Parse(@"C:\Games\Aphelion\Engine\Win64").FirstNameBelow(FolderPath.Parse(@"C:\Games")));

    [Fact]
    public void FirstNameBelow_TheFolderItself_IsItsOwnName() => Assert.Equal("Aphelion", FolderPath.Parse(@"C:\Games\Aphelion").FirstNameBelow(FolderPath.Parse(@"c:\games\aphelion\")));

    [Fact]
    public void FirstNameBelow_DriveRootItself_IsTheRoot() => Assert.Equal(@"D:\", FolderPath.Parse(@"D:\").FirstNameBelow(FolderPath.Parse(@"D:\")));

    [Fact]
    public void FirstNameBelow_FolderOutsideIt_Throws() => Assert.Throws<ArgumentException>(() => FolderPath.Parse(@"C:\GamesOther\X").FirstNameBelow(FolderPath.Parse(@"C:\Games")));

    // --- Minimal ---

    [Fact]
    public void Minimal_OneFolderInSeveralSpellings_KeepsTheFirst()
    {
        var first = FolderPath.Parse(@"D:\Games");

        var result = FolderPath.Minimal(new[] { first, FolderPath.Parse(@"d:\games\"), FolderPath.Parse("D:/Games") });

        Assert.Same(first, Assert.Single(result));
    }

    [Theory]
    [InlineData(@"D:\", @"D:\Games")]
    [InlineData(@"D:\Games", @"D:\")]
    public void Minimal_NestedEitherWay_KeepsTheOuter(string a, string b) => Assert.Equal(@"D:\", Assert.Single(FolderPath.Minimal(new[] { FolderPath.Parse(a), FolderPath.Parse(b) })).ToString());

    [Fact]
    public void Minimal_KeepsTheSurvivorsInTheirOrder()
    {
        var result = FolderPath.Minimal(new[] { @"E:\Stuff", @"D:\Games", @"C:\Games", @"D:\", @"C:\GamesOther", @"E:\Stuff\Deep" }.Select(FolderPath.Parse));

        Assert.Equal(new[] { @"E:\Stuff", @"C:\Games", @"D:\", @"C:\GamesOther" }, result.Select(folder => folder.ToString()));
    }
}
