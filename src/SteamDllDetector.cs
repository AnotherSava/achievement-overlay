using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Text;
using AchievementOverlay.GbeConfig;
using AchievementOverlay.GbeOverlay;

namespace AchievementOverlay;

/// <summary>Which of <see cref="SteamDllDetector"/>'s rules named a game's Steam DLL.</summary>
public enum SteamDllRule
{
    EmulatorSteamApi,
    ColdClientLoader,
    ValveSteamApi,
    OnlyEmulatorDll,
    OnlySteamApi
}

/// <summary>The Steam DLL a game loads, and the rule that found it.</summary>
public sealed record LoadedSteamDll(string Path, SteamDllRule Rule)
{
    /// <summary>The rule as the log spells it.</summary>
    public string RuleName => Rule switch
    {
        SteamDllRule.EmulatorSteamApi => "emulator-steam-api",
        SteamDllRule.ColdClientLoader => "coldclient-loader",
        SteamDllRule.ValveSteamApi => "valve-steam-api",
        SteamDllRule.OnlyEmulatorDll => "only-emulator-dll",
        _ => "only-steam-api"
    };

    public bool Is64 => SteamDllDetector.Is64(Path);

    /// <summary>The <c>steam_settings</c> folder the emulator in this DLL reads: the one beside it.</summary>
    public string SettingsDir => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "steam_settings");
}

/// <summary>The detector's answer: the DLL a game loads, or a sentence saying why no rule decided.</summary>
public sealed record SteamDllDetection(LoadedSteamDll? Loaded, string? Undecided)
{
    public static SteamDllDetection Found(string path, SteamDllRule rule) => new(new LoadedSteamDll(path, rule), null);

    public static SteamDllDetection NotFound(string why) => new(null, why);
}

/// <summary>
/// Works out which Steam DLL a game loads from the files in its folder, and so which
/// <c>steam_settings</c> folder its emulator reads: GBE reads the one beside its own DLL. The rules,
/// their order and the layouts behind each are in <c>docs/pages/development/steam-dll-detection.md</c>,
/// which has to change together with <see cref="Detect"/>.
/// </summary>
public static class SteamDllDetector
{
    private const string LoaderIni = "ColdClientLoader.ini";
    private const string ValveCompany = "Valve Corporation";

    private static readonly string[] SteamApiNames = ["steam_api64.dll", "steam_api.dll"];
    private static readonly string[] SteamClientNames = ["steamclient64.dll", "steamclient.dll"];
    private static readonly string[] ImporterExtensions = [".exe", ".dll", ".node"];

    // Unreal Engine loads the Steam DLL from here by explicit path, so nothing beside it imports it.
    private static readonly string[] UnrealSteamworks = ["Engine", "Binaries", "ThirdParty", "Steamworks"];

    /// <summary>
    /// The Steam DLL the game under <paramref name="gameDir"/> loads. A rule whose candidates tie stops
    /// the detection rather than passing to the next rule, since a later rule could only guess.
    /// </summary>
    public static SteamDllDetection Detect(string gameDir)
    {
        var dlls = SteamApiNames.Concat(SteamClientNames)
            .SelectMany(name => Directory.EnumerateFiles(gameDir, name, AppUtilities.RecursiveScan))
            .ToList();
        if (dlls.Count == 0)
            return SteamDllDetection.NotFound("no Steam DLL in the game folder");

        var importsByFolder = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var emulator = dlls.Where(IsEmulatorDll).ToList();
        var loading = dlls.Where(dll => IsSteamApi(dll) && Loads(dll, importsByFolder)).ToList();

        if (PickShallowest(gameDir, loading.Where(emulator.Contains), SteamDllRule.EmulatorSteamApi) is { } byEmulator)
            return byEmulator;

        var targets = Directory.EnumerateFiles(gameDir, LoaderIni, AppUtilities.RecursiveScan)
            .Select(LoaderTarget)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (targets.Count > 1)
            return SteamDllDetection.NotFound($"{LoaderIni} files name different DLLs: {Quote(gameDir, targets)}");
        if (targets.Count == 1)
            return SteamDllDetection.Found(targets[0], SteamDllRule.ColdClientLoader);

        if (PickShallowest(gameDir, loading.Where(dll => !emulator.Contains(dll)), SteamDllRule.ValveSteamApi) is { } byValve)
            return byValve;

        var emulatorFolders = emulator.GroupBy(Path.GetDirectoryName, StringComparer.OrdinalIgnoreCase).ToList();
        if (emulatorFolders.Count == 1)
            return SteamDllDetection.Found(Prefer64(emulatorFolders[0]), SteamDllRule.OnlyEmulatorDll);

        // A clean install that loads the DLL through P/Invoke, ctypes or LoadLibrary has nothing importing it.
        var steamApiFolders = dlls.Where(IsSteamApi).GroupBy(Path.GetDirectoryName, StringComparer.OrdinalIgnoreCase).ToList();
        if (steamApiFolders.Count == 1)
            return SteamDllDetection.Found(Prefer64(steamApiFolders[0]), SteamDllRule.OnlySteamApi);

        return SteamDllDetection.NotFound($"no rule decides between {Quote(gameDir, dlls)}");
    }

    /// <summary>
    /// The candidate in the shallowest folder, 64-bit over 32-bit within it — a crack or repack keeps
    /// copies of the executable and DLL in subfolders such as <c>_crack</c>, and the game runs from the
    /// copy above them. Null when there are no candidates; a tie between folders at one depth is undecided.
    /// </summary>
    private static SteamDllDetection? PickShallowest(string gameDir, IEnumerable<string> candidates, SteamDllRule rule)
    {
        var folders = candidates.GroupBy(Path.GetDirectoryName, StringComparer.OrdinalIgnoreCase).ToList();
        if (folders.Count == 0)
            return null;

        var depth = folders.Min(Depth);
        var shallowest = folders.Where(folder => Depth(folder) == depth).ToList();
        return shallowest.Count == 1
            ? SteamDllDetection.Found(Prefer64(shallowest[0]), rule)
            : SteamDllDetection.NotFound($"Steam DLLs load from {shallowest.Count} folders at one depth: {Quote(gameDir, shallowest.Select(folder => folder.Key!))}");

        static int Depth(IGrouping<string?, string> folder) => folder.Key!.Count(c => c == Path.DirectorySeparatorChar);
    }

    private static string Prefer64(IEnumerable<string> sameFolder) => sameFolder.OrderByDescending(Is64).First();

    /// <summary>True for the 64-bit name of a Steam DLL, <c>steam_api64.dll</c> or <c>steamclient64.dll</c>.</summary>
    public static bool Is64(string dll) => Path.GetFileNameWithoutExtension(dll).EndsWith("64", StringComparison.Ordinal);

    private static bool IsSteamApi(string dll) => SteamApiNames.Contains(Path.GetFileName(dll), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True for a Steam DLL that is not Valve's. CODEX and RUNE keep Valve's <c>steam_api64.dll</c>,
    /// version info included, and are known by the <c>steam_emu.ini</c> beside it instead.
    /// </summary>
    private static bool IsEmulatorDll(string dll)
    {
        if (File.Exists(Path.Combine(Path.GetDirectoryName(dll)!, SceneEmulatorDetector.EmuIni)))
            return true;
        if (FileVersionInfo.GetVersionInfo(dll).CompanyName != ValveCompany)
            return true;
        return SceneEmulatorDetector.IsGbe(File.ReadAllBytes(dll));
    }

    private static bool Loads(string steamApi, Dictionary<string, HashSet<string>> importsByFolder)
    {
        var folder = Path.GetDirectoryName(steamApi)!;
        if (IsEngineLoadPath(folder))
            return true;

        if (!importsByFolder.TryGetValue(folder, out var imports))
            importsByFolder[folder] = imports = ImportsOfFolder(folder);
        return imports.Contains(Path.GetFileName(steamApi));
    }

    /// <summary>
    /// True for a folder an engine loads the Steam DLL from by explicit path, which leaves nothing beside
    /// it importing it: Unreal's <c>Engine\Binaries\ThirdParty\Steamworks\…</c>, and Unity's plugin
    /// folder, <c>&lt;Game&gt;_Data\Plugins\…</c>.
    /// </summary>
    private static bool IsEngineLoadPath(string folder)
    {
        var names = folder.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var i = 0; i < names.Length; i++)
        {
            if (names.Skip(i).Take(UnrealSteamworks.Length).SequenceEqual(UnrealSteamworks, StringComparer.OrdinalIgnoreCase))
                return true;
            if (i + 1 < names.Length && names[i].EndsWith("_Data", StringComparison.OrdinalIgnoreCase) && names[i + 1].Equals("Plugins", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Every DLL name imported by an importer directly inside <paramref name="folder"/>.</summary>
    private static HashSet<string> ImportsOfFolder(string folder)
    {
        var imports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(file);
            if (ImporterExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase) && !SteamApiNames.Concat(SteamClientNames).Contains(name, StringComparer.OrdinalIgnoreCase))
                imports.UnionWith(ReadImports(file));
        }
        return imports;
    }

    /// <summary>
    /// The DLL names a PE file imports, delay-loaded ones included. A file that is not a readable PE
    /// image imports nothing, which is also what a packed executable's import table often says.
    /// </summary>
    public static IReadOnlySet<string> ReadImports(string path)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (pe.PEHeaders.PEHeader is not { } header)
                return names;

            // IMAGE_IMPORT_DESCRIPTOR is five ints with the name's RVA fourth; a zero name ends the table.
            ReadDescriptors(pe, header.ImportTableDirectory.RelativeVirtualAddress, 5, entry => entry[3]);
            // IMAGE_DELAYLOAD_DESCRIPTOR is eight ints: attributes, then the name, a VA unless bit 0 says RVA.
            ReadDescriptors(pe, header.DelayImportTableDirectory.RelativeVirtualAddress, 8, entry => entry[1] == 0 || (entry[0] & 1) != 0 ? entry[1] : (int)(entry[1] - (long)header.ImageBase));
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return names;
        }
        return names;

        void ReadDescriptors(PEReader pe, int tableRva, int ints, Func<int[], int> nameRva)
        {
            if (tableRva == 0)
                return;
            var reader = pe.GetSectionData(tableRva).GetReader();
            while (reader.RemainingBytes >= ints * 4)
            {
                var entry = Enumerable.Range(0, ints).Select(_ => reader.ReadInt32()).ToArray();
                var rva = nameRva(entry);
                if (rva == 0)
                    return;
                if (ReadAsciiz(pe, rva) is { } name)
                    names.Add(name);
            }
        }
    }

    private static string? ReadAsciiz(PEReader pe, int rva)
    {
        if (rva <= 0)
            return null;
        var reader = pe.GetSectionData(rva).GetReader();
        var bytes = new List<byte>();
        while (reader.RemainingBytes > 0 && reader.ReadByte() is var b && b != 0)
            bytes.Add(b);
        return bytes.Count > 0 ? Encoding.ASCII.GetString(bytes.ToArray()) : null;
    }

    /// <summary>The <c>steamclient</c> a <c>ColdClientLoader.ini</c> names, resolved from the ini's folder, when it exists.</summary>
    private static string? LoaderTarget(string ini)
    {
        var parsed = IniFile.Parse(File.ReadAllText(ini));
        var dll = parsed.Get("SteamClient", "SteamClient64Dll") ?? parsed.Get("SteamClient", "SteamClientDll");
        if (string.IsNullOrWhiteSpace(dll))
            return null;
        var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ini)!, dll.Trim()));
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// The paths relative to the game folder. The undecided sentence reaches a diagnostic report, which
    /// drops a line naming an absolute path it cannot vouch for, and the game folder can be a configured
    /// root or a prefix of a sibling game's, so it is never named itself.
    /// </summary>
    private static string Quote(string gameDir, IEnumerable<string> paths) => string.Join(", ", paths.Select(path => $"'{Path.GetRelativePath(gameDir, path)}'"));
}
