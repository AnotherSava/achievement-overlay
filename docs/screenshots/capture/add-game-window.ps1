# Opens the Add game wizard, walks it to its Ready page, and captures it.
# Driven by add-game-window.sh, which deploys first. Output path comes from $env:CAPTURE_OUT.
#
# THIS SCRIPT WRITES NOTHING TO ANY GAME. The wizard backs up and replaces a game's steam_api64.dll
# only when its final button is pressed, and that button is this script's stop condition rather than
# a step it takes: the Next button relabels itself "Add game" on the Ready page, so the loop reads
# the label before every click and stops on the one that would act. Closing the window afterwards is
# the wizard's cancel.

$ErrorActionPreference = 'Stop'

# The subject of the shot: a game with a Steam DLL and no steam_settings of its own, so the Ready
# page shows the ordinary run rather than the "a configuration already exists" warning. Keep this
# anchor when re-shooting - the prose beside the image describes the normal run - but it is one
# machine's install path, so another machine points CAPTURE_GAME_DIR at its own equivalent rather
# than editing this line. The entry is policy=auto, so a re-capture can run with nobody watching:
# failing loudly on a missing folder is the point.
$GameDir = $env:CAPTURE_GAME_DIR
if (-not $GameDir) { $GameDir = 'C:\Games\Horizon Zero Dawn Remastered' }

Add-Type @"
using System;using System.Runtime.InteropServices;
public class AddGameWin {
  public delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(P f, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  // Matched on the exact title: this process can also own a Settings and a Report a problem window.
  public static IntPtr Find(uint want) { IntPtr f = IntPtr.Zero;
    EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p);
      if (p==want && IsWindowVisible(h)) {
        var sb = new System.Text.StringBuilder(300); GetWindowText(h, sb, 300);
        if (sb.ToString() == "Add game") { f = h; return false; } }
      return true; }, IntPtr.Zero); return f; }
}
"@

. "$PSScriptRoot/lib/ui-automation.ps1"
. "$PSScriptRoot/lib/window-capture.ps1"
Enable-CaptureDpiAwareness

if (-not (Test-Path -LiteralPath $GameDir)) { throw "Game folder not found: $GameDir" }

$proc = (Get-Process AchievementOverlay).Id

if ([AddGameWin]::Find([uint32]$proc) -eq [IntPtr]::Zero) {
  Open-TrayMenu | Out-Null
  $item = Find-ByName -ControlType ([System.Windows.Automation.ControlType]::MenuItem) -Name "Add game*"
  if (-not $item) { throw "Add game item not found in the tray menu." }
  Click-Element -Element $item
}

$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 40; $i++) { Start-Sleep -Milliseconds 250; $hwnd = [AddGameWin]::Find([uint32]$proc); if ($hwnd -ne [IntPtr]::Zero) { break } }
if ($hwnd -eq [IntPtr]::Zero) { throw "Add game window never appeared." }

$ua = [System.Windows.Automation.AutomationElement]
$scope = [System.Windows.Automation.TreeScope]
$win = $ua::FromHandle($hwnd)

function Get-VisibleControl {
  param([Parameter(Mandatory)] $Type, [string[]] $Names)

  $condition = New-Object System.Windows.Automation.PropertyCondition($ua::ControlTypeProperty, $Type)
  foreach ($element in $win.FindAll($scope::Descendants, $condition)) {
    # Every wizard page is built up front and hidden, so the tree holds boxes and buttons belonging
    # to pages that are not on screen. Offscreen is what separates them.
    if ($element.Current.IsOffscreen) { continue }
    if ($Names -and $element.Current.Name -notin $Names) { continue }
    return $element
  }
  return $null
}

# Typed into the box rather than driven through Browse: FolderBrowserDialog is a native modal that
# blocks this thread, and nothing inside it can be reached by name.
$box = Get-VisibleControl -Type ([System.Windows.Automation.ControlType]::Edit)
if (-not $box) { throw "Game folder box not found on the first page." }
$box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($GameDir)

# Walk to Ready. The cap is the second guard after the label check: a page that never advances fails
# the run rather than clicking forever.
$reached = $false
for ($step = 0; $step -lt 8; $step++) {
  $next = $null
  # The folder scan and, on later pages, the Steam schema fetch both gate the button, so wait for it
  # to come back rather than clicking into a disabled control and counting that as a step.
  for ($wait = 0; $wait -lt 60; $wait++) {
    $next = Get-VisibleControl -Type ([System.Windows.Automation.ControlType]::Button) -Names @('Next', 'Add game')
    if ($next -and $next.Current.IsEnabled) { break }
    Start-Sleep -Milliseconds 500
  }
  if (-not $next) { throw "Neither a Next nor an Add game button is visible on the wizard." }
  if (-not $next.Current.IsEnabled) { throw "The wizard's Next button never became enabled." }

  if ($next.Current.Name -eq 'Add game') { $reached = $true; break }
  Click-Element -Element $next
  Start-Sleep -Milliseconds 800
}
if (-not $reached) { throw "The wizard never reached its Ready page." }
Start-Sleep -Milliseconds 900

# CropToOpaque: this window is opaque, so its rounded bottom corners are its own border curving into
# the client rectangle, which alpha recovery preserves rather than removes.
Save-WindowCapture -Hwnd $hwnd -Path $env:CAPTURE_OUT -CropToOpaque

# WM_CLOSE is the wizard's cancel. Nothing was written, so nothing is left behind.
[AddGameWin]::PostMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
