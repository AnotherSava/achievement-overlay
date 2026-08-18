using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AchievementOverlay;

/// <summary>
/// A system-wide low-level keyboard hook, installed only while a shortcut is being captured.
/// It is the only way to see a combination something else has already claimed globally — another
/// app's RegisterHotKey, or an Explorer "Shortcut key" set on a .lnk — because those outrank the
/// focused window and would otherwise fire instead of the keystroke reaching the field. This hook
/// runs ahead of both and can swallow the key so the owner never sees it.
/// </summary>
internal sealed class LowLevelKeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    // Held in a field so the GC cannot collect the delegate while Windows still holds the pointer.
    private readonly LowLevelKeyboardProc _proc;
    private readonly Func<Keys, bool> _onKeyDown;
    private IntPtr _hookId;
    private Keys _consumedKey = Keys.None;

    public bool IsInstalled => _hookId != IntPtr.Zero;

    /// <param name="onKeyDown">
    /// Returns true to consume the key, which suppresses it system-wide. It runs on the UI thread
    /// inside the hook, so it must be quick and must not throw.
    /// </param>
    public LowLevelKeyboardHook(Func<Keys, bool> onKeyDown)
    {
        _onKeyDown = onKeyDown;
        _proc = HookProc;
        _hookId = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
        if (_hookId == IntPtr.Zero)
            Logger.Warn($"Could not install the keyboard hook (error {Marshal.GetLastWin32Error()}) — a shortcut already claimed by another app cannot be captured.");
    }

    /// <summary>True while the key is physically held, read from the async state the hook sees.</summary>
    public static bool IsKeyDown(Keys key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = (int)wParam;
            var key = (Keys)Marshal.ReadInt32(lParam); // vkCode is the first field of KBDLLHOOKSTRUCT

            if (message is WmKeyDown or WmSysKeyDown)
            {
                if (Consume(key))
                {
                    _consumedKey = key;
                    return 1;
                }
            }
            else if (message is WmKeyUp or WmSysKeyUp && key == _consumedKey)
            {
                // Swallow the matching release too, so nothing downstream sees half a keystroke.
                _consumedKey = Keys.None;
                return 1;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    /// <summary>
    /// Every keystroke on the machine passes through here, so an exception escaping the callback
    /// would take down typing everywhere, not just this dialog. Let the key through instead.
    /// </summary>
    private bool Consume(Keys key)
    {
        try
        {
            return _onKeyDown(key);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Keyboard hook callback failed: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_hookId == IntPtr.Zero)
            return;

        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
