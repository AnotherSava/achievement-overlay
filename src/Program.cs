using System.Runtime.InteropServices;

// P/Invoke targets are looked up in System32 only, so a DLL of the same name placed beside the exe is never loaded in their place.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace AchievementOverlay;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, "AchievementOverlay_SingleInstance", out var isNew);
        if (!isNew)
            return;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        // Initialize WPF application for dispatcher support
        // (needed for WPF overlay windows within WinForms lifecycle)
        _ = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };

        using var context = new TrayApplicationContext();
        Application.Run(context);
    }
}
