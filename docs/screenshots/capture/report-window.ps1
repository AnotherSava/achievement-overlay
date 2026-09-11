# Opens the Report a problem window on its App config page and captures it.
# Driven by report-window.sh, which deploys first. Output path comes from $env:CAPTURE_OUT.

$ErrorActionPreference = 'Stop'

Add-Type @"
using System;using System.Runtime.InteropServices;
public class ReportWin {
  public delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(P f, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct R { public int Left, Top, Right, Bottom; }
  // By title, not by width: this process can have the settings window open too, and both are wide.
  public static IntPtr Find(uint want) { IntPtr f = IntPtr.Zero;
    EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p);
      if (p==want && IsWindowVisible(h)) {
        var sb = new System.Text.StringBuilder(300); GetWindowText(h, sb, 300);
        if (sb.ToString().Contains("Report a problem")) { f = h; return false; } }
      return true; }, IntPtr.Zero); return f; }
}
"@

. "$PSScriptRoot/lib/ui-automation.ps1"
. "$PSScriptRoot/lib/window-capture.ps1"
Enable-CaptureDpiAwareness

$proc = (Get-Process AchievementOverlay).Id

if ([ReportWin]::Find([uint32]$proc) -eq [IntPtr]::Zero) {
  # Open-TrayMenu returns the Settings item; the menu it opened holds every other item too.
  Open-TrayMenu | Out-Null
  $item = Find-ByName -ControlType ([System.Windows.Automation.ControlType]::MenuItem) -Name "Report a problem*"
  if (-not $item) { throw "Report a problem item not found in the tray menu." }
  Click-Element -Element $item
}

$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 40; $i++) { Start-Sleep -Milliseconds 250; $hwnd = [ReportWin]::Find([uint32]$proc); if ($hwnd -ne [IntPtr]::Zero) { break } }
if ($hwnd -eq [IntPtr]::Zero) { throw "Report a problem window never appeared." }

# App config rather than the first page: it is the part whose one line, its switch and a redacted
# key are all visible at once, so the shot shows what the window is for.
Select-NavPage -Hwnd $hwnd -Name "App config"

# CropToOpaque, for the same reason as the settings window: this one is opaque, so its rounded
# bottom corners are its own border curving inward rather than background to be removed.
Save-WindowCapture -Hwnd $hwnd -Path $env:CAPTURE_OUT -CropToOpaque

# Leave nothing modal on screen. The window saves nothing on its own, so closing it discards nothing.
[ReportWin]::PostMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
