using System.Diagnostics;
using System.IO;

namespace AchievementOverlay.GbeConfig;

/// <summary>
/// Generates <c>steam_interfaces.txt</c> by running GBE's bundled
/// <c>generate_interfaces_x64.exe</c> against the <b>original</b> Steam DLL. The tool
/// writes its output to the current working directory, so it's run in a temp dir and
/// the result is moved into <c>steam_settings/</c>.
/// </summary>
public static class InterfaceGenerator
{
    public static async Task<bool> GenerateAsync(
        string generateInterfacesExe, string originalDllPath, string steamSettingsDir, CancellationToken ct = default)
    {
        if (!File.Exists(generateInterfacesExe))
            throw new FileNotFoundException("generate_interfaces tool not found", generateInterfacesExe);
        if (!File.Exists(originalDllPath))
            throw new FileNotFoundException("Original Steam DLL not found", originalDllPath);

        var workDir = Path.Combine(Path.GetTempPath(), "ao-interfaces-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = generateInterfacesExe,
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(originalDllPath);

            using var process = Process.Start(psi);
            if (process == null)
                return false;

            await process.WaitForExitAsync(ct);

            var produced = Path.Combine(workDir, "steam_interfaces.txt");
            if (!File.Exists(produced))
                return false;

            Directory.CreateDirectory(steamSettingsDir);
            var target = Path.Combine(steamSettingsDir, "steam_interfaces.txt");
            File.Copy(produced, target, overwrite: true);
            return true;
        }
        finally
        {
            try { Directory.Delete(workDir, true); } catch (IOException) { /* best-effort cleanup */ }
        }
    }
}
