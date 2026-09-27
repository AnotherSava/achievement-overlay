using System.Runtime.InteropServices;

// P/Invoke targets are looked up in System32 only, so a DLL of the same name placed beside the exe is never loaded in their place.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace AchievementOverlay;

internal static class Program
{
    /// <summary>
    /// Set while the failure message box is open. The box pumps messages, so a failure that repeats —
    /// a timer tick, a file event — would otherwise open one box per occurrence; each is still logged.
    /// </summary>
    private static bool _showingFailure;

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, "AchievementOverlay_SingleInstance", out var isNew);
        if (!isNew)
            return;

        // Open for the whole process, so the handlers below record a failure however early or late it comes.
        Logger.Init();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportAndContinue("Unhandled exception on the UI thread", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogUnhandled("Unhandled exception, the process is terminating", e.ExceptionObject);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        // Initialize WPF application for dispatcher support
        // (needed for WPF overlay windows within WinForms lifecycle)
        var wpfApplication = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        wpfApplication.DispatcherUnhandledException += (_, e) =>
        {
            ReportAndContinue("Unhandled exception on the WPF dispatcher", e.Exception);
            e.Handled = true;
        };

        try
        {
            using var context = new TrayApplicationContext();
            Application.Run(context);
        }
        finally
        {
            Logger.Close();
        }
    }

    /// <summary>Logs on one line, because a diagnostic report filters the log line by line.</summary>
    private static void LogUnhandled(string what, object exception) => Logger.Error($"{what}: {exception.ToString()?.ReplaceLineEndings(" | ")}");

    /// <summary>Logs a UI-thread failure and tells the user, for a caller that then lets the app carry on.</summary>
    private static void ReportAndContinue(string what, Exception exception)
    {
        LogUnhandled(what, exception);
        if (_showingFailure)
            return;

        _showingFailure = true;
        try
        {
            MessageBox.Show($"Something failed: {exception.Message}\r\n\r\nThe app is still running. The log has the details: {Logger.LogPath}", "Achievement Overlay", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _showingFailure = false;
        }
    }
}
