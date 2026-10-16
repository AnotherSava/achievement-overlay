using System.IO;

namespace AchievementOverlay.GbeConfig;

/// <summary>What the Add game wizard does with the Steam DLL a game loads.</summary>
public enum InstallAction
{
    /// <summary>Back up the <c>steam_api</c> and put GBE's regular build in its place, unless a CODEX/RUNE emulator in it is kept.</summary>
    ReplaceSteamApi,

    /// <summary>The DLL is already GBE, behind a ColdClientLoader: write the schema beside it and install nothing.</summary>
    KeepGbe
}

/// <summary>The DLL the wizard works on, and what it does there.</summary>
public sealed record InstallTarget(LoadedSteamDll Dll, InstallAction Action)
{
    /// <summary>
    /// Why GBE cannot be installed here, or null when it can. The wizard installs only GBE's 64-bit
    /// build, which keeping the game's own emulator does not need.
    /// </summary>
    public string? InstallRefusal => Action == InstallAction.ReplaceSteamApi && !Dll.Is64
        ? $"This game loads the 32-bit {Path.GetFileName(Dll.Path)}, and the wizard installs only GBE's 64-bit build."
        : null;

    /// <summary>
    /// The target for the game under <paramref name="gameDir"/>, or a sentence saying why the wizard
    /// cannot configure it. The form and <see cref="GbeConfigGenerator"/> both call this, so the folder the
    /// user is told about is the one the generator writes to.
    /// </summary>
    public static (InstallTarget? Target, string? Refusal) Choose(string gameDir)
    {
        var detection = SteamDllDetector.Detect(gameDir);
        if (detection.Loaded is not { } dll)
            return (null, $"Couldn't tell which Steam library this game loads: {detection.Undecided}.");

        var name = Path.GetFileName(dll.Path);
        if (name.StartsWith("steam_api", StringComparison.OrdinalIgnoreCase))
            return (new InstallTarget(dll, InstallAction.ReplaceSteamApi), null);

        if (SceneEmulatorDetector.IsGbe(File.ReadAllBytes(dll.Path)))
            return (new InstallTarget(dll, InstallAction.KeepGbe), null);

        return (null, $"This game loads Steam through {dll.Path}, a steamclient that is not GBE. The wizard installs only GBE's "
            + "steam_api64.dll, which can't stand in for it.");
    }
}
