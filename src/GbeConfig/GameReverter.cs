using System.IO;

namespace AchievementOverlay.GbeConfig;

/// <summary>What a revert did, and what it left in place with the reason, each as a sentence for the user.</summary>
public sealed record RevertResult(IReadOnlyList<string> Done, IReadOnlyList<string> Kept);

/// <summary>
/// Puts a game back as it was before Add game set it up, from the <see cref="SetupRecord"/> in its
/// <c>steam_settings/</c>. A game set up before the record existed gets its Steam DLL back from the
/// wizard's <c>.original</c> backup and keeps every file, since nothing says which of them came with the
/// game. Unlock history in the save folders is never touched: it is the user's, not the setup's.
/// </summary>
public static class GameReverter
{
    /// <summary>The extension the wizard backs a replaced Steam DLL up under.</summary>
    public const string BackupExtension = ".original";

    /// <summary>Why the game in <paramref name="settingsDir"/> cannot be reverted, or null when it can.</summary>
    public static string? CannotRevert(string settingsDir)
    {
        if (File.Exists(Path.Combine(settingsDir, SetupRecord.FileName)))
            return null;
        return LegacyDll(settingsDir) == null
            ? "Add game left no record or backup here, so there is nothing it knows how to undo."
            : null;
    }

    /// <summary>
    /// Reverts the game in <paramref name="settingsDir"/>. The DLL goes first: it is the step a running game
    /// blocks, and failing there leaves every config file as it was. Throws on IO failure.
    /// </summary>
    public static RevertResult Revert(string settingsDir)
    {
        var done = new List<string>();
        var kept = new List<string>();
        var gameDir = Path.GetDirectoryName(Path.GetFullPath(settingsDir))!;
        var record = SetupRecord.Load(settingsDir);

        if (record == null)
        {
            var dll = LegacyDll(settingsDir) ?? throw new InvalidOperationException(CannotRevert(settingsDir));
            RestoreDll(dll, dll + BackupExtension, done);
            kept.Add("The files in steam_settings are kept: this game was set up before Add game recorded which files it wrote.");
            return new RevertResult(done, kept);
        }

        if (record.Dll is { } dllName)
        {
            var dll = Path.Combine(gameDir, dllName);
            var backup = record.DllBackup is { } backupName ? Path.Combine(gameDir, backupName) : null;
            if (backup != null && File.Exists(backup))
                RestoreDll(dll, backup, done);
            else
                kept.Add($"{dllName} stays GBE's: no backup of the original was made when it was replaced.");
        }

        foreach (var relative in record.Overwritten)
            File.Copy(Path.Combine(settingsDir, SetupRecord.BackupFolder, relative), Path.Combine(settingsDir, relative), overwrite: true);
        foreach (var relative in record.Created)
            File.Delete(Path.Combine(settingsDir, relative));
        if (record.Overwritten.Count > 0)
            done.Add($"Put back {AppUtilities.CountOf(record.Overwritten.Count, "file")} in steam_settings as the game had them.");
        if (record.Created.Count > 0)
            done.Add($"Removed {AppUtilities.CountOf(record.Created.Count, "file")} Add game wrote in steam_settings.");

        var backupFolder = Path.Combine(settingsDir, SetupRecord.BackupFolder);
        if (Directory.Exists(backupFolder))
            Directory.Delete(backupFolder, recursive: true);
        File.Delete(Path.Combine(settingsDir, SetupRecord.FileName));

        RemoveFoldersLeftEmpty(settingsDir, record.Created);
        if (record.SettingsDirCreated && !Directory.EnumerateFileSystemEntries(settingsDir).Any())
        {
            Directory.Delete(settingsDir);
            done.Add("Removed the steam_settings folder Add game created.");
        }

        return new RevertResult(done, kept);
    }

    /// <summary>A game set up before the record: its Steam DLL beside <c>steam_settings/</c> with a wizard backup, or null.</summary>
    private static string? LegacyDll(string settingsDir)
    {
        var gameDir = Path.GetDirectoryName(Path.GetFullPath(settingsDir))!;
        return SteamDllDetector.SteamApiNames.Select(name => Path.Combine(gameDir, name)).FirstOrDefault(dll => File.Exists(dll) && File.Exists(dll + BackupExtension));
    }

    private static void RestoreDll(string dll, string backup, List<string> done)
    {
        File.Move(backup, dll, overwrite: true);
        done.Add($"Put back the game's own {Path.GetFileName(dll)} from {Path.GetFileName(backup)}.");
    }

    /// <summary>
    /// Removes the folders below <paramref name="root"/> that held a removed file and are empty now, deepest
    /// first. Only those: an empty folder the game shipped with is part of how it was.
    /// </summary>
    private static void RemoveFoldersLeftEmpty(string root, IEnumerable<string> removed)
    {
        var folders = removed.Select(relative => Path.GetDirectoryName(relative)).Where(dir => !string.IsNullOrEmpty(dir)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in folders.Select(relative => Path.Combine(root, relative!)).OrderByDescending(dir => dir.Length))
        {
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
    }
}
