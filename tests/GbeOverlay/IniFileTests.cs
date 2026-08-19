using AchievementOverlay.GbeOverlay;
using Xunit;

namespace AchievementOverlay.Tests.GbeOverlay;

public class IniFileTests
{
    private const string Section = "overlay::appearance";

    [Fact]
    public void Parse_ReadsAKeyInItsSection()
    {
        var ini = IniFile.Parse("[overlay::appearance]\nFont_Size=22.0\n");

        Assert.Equal("22.0", ini.Get(Section, "Font_Size"));
    }

    [Fact]
    public void Parse_KeyOutsideTheSection_IsNotFoundInIt()
    {
        var ini = IniFile.Parse("[overlay::general]\nFont_Size=22.0\n");

        Assert.Null(ini.Get(Section, "Font_Size"));
    }

    [Theory]
    [InlineData("; Font_Size=22.0")]
    [InlineData("# Font_Size=22.0")]
    [InlineData("   ; Font_Size=22.0")]
    public void Parse_CommentLines_AreIgnored(string line)
    {
        var ini = IniFile.Parse($"[overlay::appearance]\n{line}\n");

        Assert.Null(ini.Get(Section, "Font_Size"));
    }

    [Fact]
    public void Parse_TrailingHash_IsPartOfTheValue()
    {
        // GBE honours no trailing comments — a value runs to the end of its line.
        var ini = IniFile.Parse("[overlay::appearance]\nFont_Size=16 # big\n");

        Assert.Equal("16 # big", ini.Get(Section, "Font_Size"));
    }

    [Fact]
    public void Parse_QuotedValue_KeepsTheQuotes()
    {
        // Quote parsing is off in GBE's reader, so the quotes are part of the file name.
        var ini = IniFile.Parse("[overlay::appearance]\nFont_Override=\"poppins.ttf\"\n");

        Assert.Equal("\"poppins.ttf\"", ini.Get(Section, "Font_Override"));
    }

    [Fact]
    public void Parse_DuplicateKey_LastWins()
    {
        var ini = IniFile.Parse("[overlay::appearance]\nFont_Size=16\nFont_Size=22\n");

        Assert.Equal("22", ini.Get(Section, "Font_Size"));
    }

    [Fact]
    public void Parse_SectionAndKey_AreCaseInsensitive()
    {
        // The deliberate divergence: GBE drops 'font_size' because it compares stored spellings.
        var ini = IniFile.Parse("[OVERLAY::Appearance]\nfont_size=22\n");

        Assert.Equal("22", ini.Get(Section, "Font_Size"));
    }

    [Fact]
    public void Parse_KeyWithoutEquals_IsIgnored()
    {
        var ini = IniFile.Parse("[overlay::appearance]\nFont_Size\n");

        Assert.Null(ini.Get(Section, "Font_Size"));
    }

    [Fact]
    public void Parse_EmptyValue_ReadsAsAbsent()
    {
        var ini = IniFile.Parse("[overlay::appearance]\nFont_Size=\n");

        Assert.Null(ini.Get(Section, "Font_Size"));
    }

    [Fact]
    public void Parse_WhitespaceAndCarriageReturns_AreTrimmed()
    {
        var ini = IniFile.Parse("[ overlay::appearance ]\r\n  Font_Size  =  22.0  \r\n");

        Assert.Equal("22.0", ini.Get(Section, "Font_Size"));
    }

    [Fact]
    public void Parse_ValueContainingEquals_KeepsEverythingAfterTheFirstOne()
    {
        var ini = IniFile.Parse("[overlay::appearance]\nAchievement_Unlock_Datetime_Format=a=b\n");

        Assert.Equal("a=b", ini.Get(Section, "Achievement_Unlock_Datetime_Format"));
    }

    [Fact]
    public void WithFallback_KeyInBoth_KeepsTheStrongerFile()
    {
        // GBE merges its four config files with the first definition winning.
        var stronger = IniFile.Parse("[overlay::appearance]\nFont_Size=22\n");
        var weaker = IniFile.Parse("[overlay::appearance]\nFont_Size=16\n");

        Assert.Equal("22", stronger.WithFallback(weaker).Get(Section, "Font_Size"));
    }

    [Fact]
    public void WithFallback_KeyOnlyInWeaker_IsAdopted()
    {
        var stronger = IniFile.Parse("[overlay::appearance]\nFont_Size=22\n");
        var weaker = IniFile.Parse("[overlay::appearance]\nFont_Override=poppins.ttf\n");

        var merged = stronger.WithFallback(weaker);

        Assert.Equal("22", merged.Get(Section, "Font_Size"));
        Assert.Equal("poppins.ttf", merged.Get(Section, "Font_Override"));
    }

    [Fact]
    public void WithFallback_SectionOnlyInWeaker_IsAdopted()
    {
        var merged = IniFile.Empty.WithFallback(IniFile.Parse("[overlay::appearance]\nFont_Size=22\n"));

        Assert.Equal("22", merged.Get(Section, "Font_Size"));
    }

    [Fact]
    public void WithFallback_DoesNotMutateEitherSide()
    {
        var stronger = IniFile.Parse("[overlay::appearance]\nFont_Size=22\n");
        var weaker = IniFile.Parse("[overlay::appearance]\nFont_Override=poppins.ttf\n");

        stronger.WithFallback(weaker);

        Assert.Null(stronger.Get(Section, "Font_Override"));
        Assert.Null(weaker.Get(Section, "Font_Size"));
    }
}
