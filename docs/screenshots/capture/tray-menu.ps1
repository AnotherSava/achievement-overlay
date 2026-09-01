# Opens the tray context menu and captures it.
# Driven by tray-menu.sh, which deploys first. Output path comes from $env:CAPTURE_OUT.

$ErrorActionPreference = 'Stop'

Add-Type @"
using System;using System.Runtime.InteropServices;
public class TrayWin {
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
}
"@

. "$PSScriptRoot/lib/ui-automation.ps1"
. "$PSScriptRoot/lib/window-capture.ps1"
Enable-CaptureDpiAwareness

$item = Open-TrayMenu

# The menu window is the item's parent in the control view.
$menu = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($item)
$hwnd = [IntPtr]$menu.Current.NativeWindowHandle
if ($hwnd -eq [IntPtr]::Zero) { throw "Menu has no window handle to capture." }

$ua = [System.Windows.Automation.AutomationElement]
$c = New-Object System.Windows.Automation.PropertyCondition($ua::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)
$names = @()
foreach ($m in $menu.FindAll([System.Windows.Automation.TreeScope]::Children, $c)) { $names += $m.Current.Name }
Write-Output ("menu items: " + ($names -join " | "))

# WholeWindow: a popup menu's client area excludes its border, and the border is part of the menu.
# No CropToOpaque: any rounded corners and drop shadow should stay soft, so the shot drops onto a
# light or a dark page without a hard rectangle around it.
Save-WindowCapture -Hwnd $hwnd -Path $env:CAPTURE_OUT -WholeWindow

if (-not ([TrayWin]::IsWindow($hwnd) -and [TrayWin]::IsWindowVisible($hwnd))) {
  throw "The menu closed before it was captured - the backdrop stole focus from it."
}

# Dismiss the menu.
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.SendKeys]::SendWait("{ESC}")
