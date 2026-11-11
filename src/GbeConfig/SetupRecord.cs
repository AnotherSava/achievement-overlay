using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AchievementOverlay.GbeConfig;

/// <summary>
/// What Add game changed in one game, kept beside the config it wrote so <see cref="GameReverter"/> can put
/// the game back as it was. Every file the wizard writes into <c>steam_settings/</c> is recorded as created,
/// or, when one was already there, copied into <see cref="BackupFolder"/> first and recorded as overwritten:
/// a repack ships its own <c>steam_settings/</c> often enough that deleting what the wizard touched would
/// take the repack's files with it. A second run over the same game keeps the first run's entries, so a file
/// the first run created is never mistaken for one the game came with.
/// </summary>
public sealed class SetupRecord
{
    /// <summary>The record's file name inside <c>steam_settings/</c>; GBE reads only the files it knows.</summary>
    public const string FileName = "achievement-overlay.json";

    /// <summary>Where files the wizard overwrote are kept, inside <c>steam_settings/</c>.</summary>
    public const string BackupFolder = ".achievement-overlay-backup";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly Lock _lock = new();

    [JsonIgnore]
    public string SettingsDir { get; private set; } = "";

    /// <summary>Whether the wizard created <c>steam_settings/</c> itself, so a revert may remove it once it is empty.</summary>
    public bool SettingsDirCreated { get; set; }

    /// <summary>The Steam DLL the wizard replaced, as a file name beside <c>steam_settings/</c>; null when it replaced none.</summary>
    public string? Dll { get; set; }

    /// <summary>
    /// The copy of the game's own DLL, beside it; null when no backup was made. It may predate this record:
    /// an earlier setup's backup is kept as the original, since the DLL it replaced was already GBE.
    /// </summary>
    public string? DllBackup { get; set; }

    /// <summary>Files the wizard created, relative to <c>steam_settings/</c>.</summary>
    public List<string> Created { get; set; } = new();

    /// <summary>Files the wizard overwrote, relative to <c>steam_settings/</c>; each one's original is under <see cref="BackupFolder"/>.</summary>
    public List<string> Overwritten { get; set; } = new();

    /// <summary>The record in <paramref name="settingsDir"/>, or null when that folder has none.</summary>
    public static SetupRecord? Load(string settingsDir)
    {
        var path = Path.Combine(settingsDir, FileName);
        if (!File.Exists(path))
            return null;
        var record = JsonSerializer.Deserialize<SetupRecord>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException($"'{path}' is empty");
        record.SettingsDir = settingsDir;
        return record;
    }

    /// <summary>
    /// The record a run starts from: the folder's existing one, so a second run keeps what the first
    /// recorded, or a fresh one. Call before the folder is created, so a fresh record can tell whether
    /// the wizard is the one creating it.
    /// </summary>
    public static SetupRecord Begin(string settingsDir) =>
        Load(settingsDir) ?? new SetupRecord { SettingsDir = settingsDir, SettingsDirCreated = !Directory.Exists(settingsDir) };

    /// <summary>
    /// Records that the wizard is about to write <paramref name="path"/>, inside <c>steam_settings/</c>:
    /// a file already there is first copied aside, unless an earlier entry already accounts for it.
    /// Safe to call from the parallel icon downloads.
    /// </summary>
    public void BeforeWrite(string path)
    {
        var relative = Path.GetRelativePath(SettingsDir, path);
        lock (_lock)
        {
            if (Created.Contains(relative, StringComparer.OrdinalIgnoreCase) || Overwritten.Contains(relative, StringComparer.OrdinalIgnoreCase))
                return;

            if (File.Exists(path))
            {
                var backup = Path.Combine(SettingsDir, BackupFolder, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(path, backup, overwrite: true);
                Overwritten.Add(relative);
            }
            else
            {
                Created.Add(relative);
            }
        }
    }

    /// <summary>Records the DLL the wizard is replacing and its backup, unless an earlier run already recorded them.</summary>
    public void RecordDll(string dllFileName, string? backupFileName)
    {
        lock (_lock)
        {
            if (Dll != null)
                return;
            Dll = dllFileName;
            DllBackup = backupFileName;
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(Path.Combine(SettingsDir, FileName), JsonSerializer.Serialize(this, JsonOptions));
        }
    }
}
