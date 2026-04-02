using System.Windows.Forms;
using AchievementOverlay;
using Xunit;

namespace AchievementOverlay.Tests;

public class GlobalHotkeyTests
{
    [Fact]
    public void ParseHotkeyString_CtrlShiftH_ReturnsCorrectValues()
    {
        var (modifiers, vk) = GlobalHotkey.ParseHotkeyString("Ctrl+Shift+H");
        Assert.Equal(0x0002u | 0x0004u, modifiers); // MOD_CONTROL | MOD_SHIFT
        Assert.Equal((uint)Keys.H, vk);
    }

    [Fact]
    public void ParseHotkeyString_SingleKey_ReturnsNoModifiers()
    {
        var (modifiers, vk) = GlobalHotkey.ParseHotkeyString("F5");
        Assert.Equal(0u, modifiers);
        Assert.Equal((uint)Keys.F5, vk);
    }

    [Fact]
    public void ParseHotkeyString_CtrlAltDelete_ReturnsCorrectModifiers()
    {
        var (modifiers, vk) = GlobalHotkey.ParseHotkeyString("Ctrl+Alt+Delete");
        Assert.Equal(0x0002u | 0x0001u, modifiers); // MOD_CONTROL | MOD_ALT
        Assert.Equal((uint)Keys.Delete, vk);
    }

    [Fact]
    public void ParseHotkeyString_CaseInsensitive()
    {
        var (modifiers, vk) = GlobalHotkey.ParseHotkeyString("ctrl+shift+h");
        Assert.Equal(0x0002u | 0x0004u, modifiers);
        Assert.Equal((uint)Keys.H, vk);
    }

    [Fact]
    public void ParseHotkeyString_InvalidKey_ReturnsZeroVk()
    {
        var (modifiers, vk) = GlobalHotkey.ParseHotkeyString("Ctrl+Shift+H1");
        Assert.Equal(0x0002u | 0x0004u, modifiers);
        Assert.Equal(0u, vk);
    }

    [Fact]
    public void ParseHotkeyString_Escape_ReturnsEscapeKey()
    {
        var (modifiers, vk) = GlobalHotkey.ParseHotkeyString("Escape");
        Assert.Equal(0u, modifiers);
        Assert.Equal((uint)Keys.Escape, vk);
    }

    [Fact]
    public void ParseHotkeyString_ControlAlias_Works()
    {
        var (modifiers, _) = GlobalHotkey.ParseHotkeyString("Control+A");
        Assert.Equal(0x0002u, modifiers); // MOD_CONTROL
    }

    [Fact]
    public void ParseHotkeyString_WinModifier()
    {
        var (modifiers, vk) = GlobalHotkey.ParseHotkeyString("Win+E");
        Assert.Equal(0x0008u, modifiers); // MOD_WIN
        Assert.Equal((uint)Keys.E, vk);
    }

    [Fact]
    public void ParseHotkeyString_EmptyString_ReturnsZeros()
    {
        var (modifiers, vk) = GlobalHotkey.ParseHotkeyString("");
        Assert.Equal(0u, modifiers);
        Assert.Equal(0u, vk);
    }
}
