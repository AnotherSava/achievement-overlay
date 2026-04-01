using System.Windows.Forms;

namespace AchievementOverlay;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "AchievementOverlay_SingleInstance", out var isNew);
        if (!isNew)
            return;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        // Initialize WPF application for dispatcher support
        // (needed for WPF overlay windows within WinForms lifecycle)
        if (System.Windows.Application.Current == null)
        {
            new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        }

        Application.Run(new TrayApplicationContext());
    }
}
