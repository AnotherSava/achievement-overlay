using System.IO;
using System.Text;
using AchievementOverlay.GbeOverlay;

namespace AchievementOverlay.GbeConfig;

/// <summary>
/// A CODEX or RUNE Steam emulator found active beside a game's Steam DLL.
/// </summary>
/// <param name="Name">"RUNE", "CODEX", or "CODEX/RUNE" when the folder says neither.</param>
/// <param name="AppId">Its <c>[Settings] AppId</c>, or null when it has none.</param>
/// <param name="SavesFolders">The folders it writes <c>achievements.ini</c> into, raw as config stores them.</param>
/// <param name="Interfaces">The Steam interface versions its <c>[Interfaces]</c> section lists.</param>
public sealed record SceneEmulator(string Name, string? AppId, IReadOnlyList<string> SavesFolders, IReadOnlyList<string> Interfaces);

/// <summary>
/// Tells whether the CODEX/RUNE emulator is what a game's Steam DLL currently is. Its files outlive a
/// replacement — a game switched to GBE keeps <c>steam_emu.ini</c> and its <c>.rne</c>/<c>.cdx</c>
/// payloads beside the GBE DLL — so the DLL itself decides. The emulator's loader carries Valve's
/// version info and no readable marker of its own, which is why GBE is the side recognised positively.
/// </summary>
public static class SceneEmulatorDetector
{
    public const string EmuIni = "steam_emu.ini";

    /// <summary>The emulator fixes this path itself; <c>steam_emu.ini</c> has no key to move it.</summary>
    public const string RuneSaves = @"%public%\Documents\Steam\RUNE";
    public const string CodexSaves = @"%public%\Documents\Steam\CODEX";

    /// <summary>Strings only a GBE build carries: the names of the save folders it creates.</summary>
    private static readonly string[] GbeMarkers = { "GSE Saves", "Goldberg SteamEmu Saves" };

    /// <summary>
    /// The emulator active as <paramref name="dllPath"/>, or null when there is no <c>steam_emu.ini</c>
    /// beside it or the DLL is GBE.
    /// </summary>
    public static SceneEmulator? Detect(string dllPath)
    {
        var dir = Path.GetDirectoryName(dllPath)!;
        var emuIni = Path.Combine(dir, EmuIni);
        if (!File.Exists(emuIni) || IsGbe(File.ReadAllBytes(dllPath)))
            return null;

        var hasRne = Directory.EnumerateFiles(dir, "*.rne").Any();
        var hasCdx = Directory.EnumerateFiles(dir, "*.cdx").Any();
        var (name, folders) = (hasRne, hasCdx) switch
        {
            (true, false) => ("RUNE", new[] { RuneSaves }),
            (false, true) => ("CODEX", new[] { CodexSaves }),
            _ => ("CODEX/RUNE", new[] { RuneSaves, CodexSaves })
        };

        var ini = IniFile.Parse(File.ReadAllText(emuIni));
        return new SceneEmulator(name, ReadAppId(ini), folders, ini.Entries("Interfaces").Values.ToList());
    }

    /// <summary>The <c>[Settings] AppId</c> of a <c>steam_emu.ini</c>, or null when it is absent or not a number.</summary>
    public static string? ReadAppId(IniFile emuIni)
    {
        var appId = emuIni.Get("Settings", "AppId");
        return appId != null && appId.All(char.IsAsciiDigit) ? appId : null;
    }

    /// <summary>True when the DLL's bytes hold a GBE marker, in ASCII or UTF-16.</summary>
    public static bool IsGbe(ReadOnlySpan<byte> dll)
    {
        foreach (var marker in GbeMarkers)
        {
            if (dll.IndexOf(Encoding.ASCII.GetBytes(marker)) >= 0 || dll.IndexOf(Encoding.Unicode.GetBytes(marker)) >= 0)
                return true;
        }
        return false;
    }
}
