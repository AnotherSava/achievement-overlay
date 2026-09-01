# Opens the Settings window on its Notifications page and captures it.
# Driven by settings-window.sh, which deploys first. Output path comes from $env:CAPTURE_OUT.

$ErrorActionPreference = 'Stop'

Add-Type @"
using System;using System.Runtime.InteropServices;
public class SettingsWin {
  public delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(P f, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct R { public int Left, Top, Right, Bottom; }
  // The settings window is the only window this process shows anywhere near this wide.
  public static IntPtr Find(uint want) { IntPtr f = IntPtr.Zero;
    EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p);
      if (p==want && IsWindowVisible(h)) { R r; GetWindowRect(h, out r);
        if ((r.Right-r.Left) >= 1000) { f = h; return false; } }
      return true; }, IntPtr.Zero); return f; }
}
"@

. "$PSScriptRoot/lib/ui-automation.ps1"
. "$PSScriptRoot/lib/window-capture.ps1"
Enable-CaptureDpiAwareness

$proc = (Get-Process AchievementOverlay).Id

if ([SettingsWin]::Find([uint32]$proc) -eq [IntPtr]::Zero) {
  Click-Element -Element (Open-TrayMenu)
}

$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 40; $i++) { Start-Sleep -Milliseconds 250; $hwnd = [SettingsWin]::Find([uint32]$proc); if ($hwnd -ne [IntPtr]::Zero) { break } }
if ($hwnd -eq [IntPtr]::Zero) { throw "Settings window never appeared." }

# FromHandle, not a global search: a Descendants sweep can hand back a stale element.
$ua = [System.Windows.Automation.AutomationElement]
$win = $ua::FromHandle($hwnd)
$li = New-Object System.Windows.Automation.PropertyCondition($ua::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
$nav = $null
foreach ($n in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $li)) {
  if ($n.Current.Name -eq "Notifications") { $nav = $n; break }
}
if (-not $nav) { throw "Notifications page not found in the nav rail." }
$nav.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 900

# CropToOpaque: this window is opaque, so its rounded bottom corners are chrome curving into the
# client rectangle, not background that alpha recovery can remove.
Save-WindowCapture -Hwnd $hwnd -Path $env:CAPTURE_OUT -CropToOpaque

# Leave nothing modal on screen. WM_CLOSE is a cancel, so nothing is saved.
[SettingsWin]::PostMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
