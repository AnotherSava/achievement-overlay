using System.Globalization;
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
/// <param name="OriginalDll">The game's own Valve library the emulator kept beside itself, or null when there is none.</param>
/// <param name="PlayerSteamId">The 64-bit Steam ID it plays as, or null when it picks one at random or it is not known.</param>
public sealed record SceneEmulator(string Name, string? AppId, IReadOnlyList<string> SavesFolders, IReadOnlyList<string> Interfaces, string? OriginalDll = null, string? PlayerSteamId = null);

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

    /// <summary>The base every individual account's 64-bit Steam ID adds its 32-bit account ID to.</summary>
    private const ulong IndividualAccountBase = 76561197960265728UL;

    /// <summary>
    /// The account ID RUNE plays as when <c>steam_emu.ini</c> leaves <c>AccountId</c> commented out, as
    /// its template does. Measured, not documented: three RUNE games with the template's line filed their
    /// saves under 76561197960271872, which is this ID on <see cref="IndividualAccountBase"/>.
    /// </summary>
    private const ulong RuneDefaultAccountId = 6144;

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
        // RUNE keeps the game's own library under the DLL's name with a .rne extension. A CODEX .cdx is left
        // out: no CODEX install has been seen to say whether it is the original or the emulator's payload.
        var rne = Path.ChangeExtension(dllPath, ".rne");
        var originalDll = name == "RUNE" && File.Exists(rne) ? rne : null;
        var steamId = name == "RUNE" ? RunePlayerSteamId(ini) : null;
        return new SceneEmulator(name, ReadAppId(ini), folders, ini.Entries("Interfaces").Values.ToList(), originalDll, steamId);
    }

    /// <summary>
    /// The 64-bit Steam ID a RUNE <c>steam_emu.ini</c> plays as: its <c>[Settings] AccountId</c> on the
    /// individual-account base, RUNE's default when the key is absent, and null for 0 or anything
    /// unreadable, since the template says 0 picks a random ID.
    /// </summary>
    public static string? RunePlayerSteamId(IniFile emuIni)
    {
        var raw = emuIni.Get("Settings", "AccountId");
        if (raw == null)
            return (IndividualAccountBase + RuneDefaultAccountId).ToString(CultureInfo.InvariantCulture);
        return uint.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var accountId) && accountId > 0 ? (IndividualAccountBase + accountId).ToString(CultureInfo.InvariantCulture) : null;
    }

    /// <summary>The <c>[Settings] AppId</c> of a <c>steam_emu.ini</c>, or null when it is absent or not a number.</summary>
    public static string? ReadAppId(IniFile emuIni)
    {
        var appId = emuIni.Get("Settings", "AppId");
        return AppIdResolver.IsAppId(appId) ? appId : null;
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
