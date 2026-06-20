using System.ComponentModel;
using System.Diagnostics;

namespace AchievementOverlay.GbeConfig;

/// <summary>Outcome of an attempt to add Windows Defender exclusions.</summary>
public enum ExclusionResult
{
    Added,
    Cancelled, // user declined the UAC prompt
    Failed
}

/// <summary>
/// Adds Windows Defender path exclusions via the Defender PowerShell cmdlet. Modifying Defender
/// requires elevation, so this launches an elevated PowerShell (one UAC prompt) — the app itself
/// stays unelevated.
/// </summary>
public static class WindowsDefender
{
    public static async Task<ExclusionResult> AddExclusionsAsync(IEnumerable<string> paths)
    {
        // Single-quote each path for PowerShell, doubling any embedded quotes.
        var quoted = string.Join(",", paths.Select(p => "'" + p.Replace("'", "''") + "'"));
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Add-MpPreference -ExclusionPath {quoted}\"",
            UseShellExecute = true,
            Verb = "runas", // triggers the UAC elevation prompt
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            return await Task.Run(() =>
            {
                using var process = Process.Start(psi);
                if (process == null)
                    return ExclusionResult.Failed;
                process.WaitForExit();
                return process.ExitCode == 0 ? ExclusionResult.Added : ExclusionResult.Failed;
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED — UAC declined
        {
            return ExclusionResult.Cancelled;
        }
        catch (Exception)
        {
            return ExclusionResult.Failed;
        }
    }
}
