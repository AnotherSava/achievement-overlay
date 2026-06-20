using System.IO;

namespace AchievementOverlay.GbeConfig;

/// <summary>
/// A located Steam API DLL and whether it's the 64-bit variant.
/// </summary>
public sealed record SteamDll(string Path, bool Is64);

/// <summary>
/// Finds <c>steam_api64.dll</c> / <c>steam_api.dll</c> under a game directory.
/// GBE looks for <c>steam_settings/</c> adjacent to the loaded DLL — not at the game
/// root — so deeply nested UE-style paths must be handled.
/// </summary>
public static class DllLocator
{
    private const string Dll64 = "steam_api64.dll";
    private const string Dll32 = "steam_api.dll";

    /// <summary>
    /// Returns every Steam API DLL under <paramref name="gameDir"/>, excluding any
    /// previously generated GBE backup (<c>*.original</c>). 64-bit DLLs come first.
    /// </summary>
    public static List<SteamDll> FindAll(string gameDir)
    {
        var results = new List<SteamDll>();
        if (!Directory.Exists(gameDir))
            return results;

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var path in Directory.EnumerateFiles(gameDir, "steam_api*.dll", options))
        {
            var name = Path.GetFileName(path);
            if (name.Equals(Dll64, StringComparison.OrdinalIgnoreCase))
                results.Add(new SteamDll(path, true));
            else if (name.Equals(Dll32, StringComparison.OrdinalIgnoreCase))
                results.Add(new SteamDll(path, false));
        }

        return results
            .OrderByDescending(d => d.Is64)
            .ThenBy(d => d.Path.Length)
            .ToList();
    }

    /// <summary>
    /// Picks the primary DLL to configure. Prefers the 64-bit variant; among equal
    /// architectures prefers the shallowest path. Returns null if none found.
    /// </summary>
    public static SteamDll? SelectPrimary(IReadOnlyList<SteamDll> candidates) => candidates.FirstOrDefault();
}
