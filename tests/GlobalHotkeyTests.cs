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

    [Theory]
    [InlineData(Keys.H)]
    [InlineData(Keys.D1)]
    [InlineData(Keys.F5)]
    [InlineData(Keys.OemQuestion)] // aliased: ToString() gives "Oem2", which must still parse back
    [InlineData(Keys.NumPad7)]
    [InlineData(Keys.Space)]
    [InlineData(Keys.Oemtilde)]
    public void ParseHotkeyString_RoundTripsCapturedKeyNames(Keys key)
    {
        // The settings dialog builds its shortcut string from Keys.ToString(), so every name it can
        // produce has to parse back — otherwise a shortcut the user picked in the GUI would be saved
        // and then silently fail to register.
        var (modifiers, vk) = GlobalHotkey.ParseHotkeyString($"Ctrl+Shift+Alt+{key}");
        Assert.Equal(0x0002u | 0x0004u | 0x0001u, modifiers);
        Assert.Equal((uint)key, vk);
    }
}
