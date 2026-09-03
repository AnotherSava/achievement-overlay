# Driving this app's tray icon and its menu through UI Automation, for capture scripts.
#
# Dot-source this alongside window-capture.ps1. Kept separate from it because capturing pixels and
# driving controls are different jobs, and only some captures need the second.
#
# Call Enable-CaptureDpiAwareness before anything else: a DPI-unaware process is handed *virtualized*
# coordinates, so on a scaled display the tray button's rect reads as a position that is nowhere on
# screen and every click lands somewhere else.

Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class UiInput {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);

  public const uint LEFT_DOWN = 0x0002, LEFT_UP = 0x0004, RIGHT_DOWN = 0x0008, RIGHT_UP = 0x0010;
}
"@

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

function Enable-CaptureDpiAwareness {
  [UiInput]::SetProcessDPIAware() | Out-Null
}

<#
.SYNOPSIS
  First element of the given control type whose Name matches a wildcard pattern, searched from the
  desktop root.
.DESCRIPTION
  Searched one top-level window at a time rather than as a single Descendants sweep of the desktop.
  The sweep is what the obvious implementation does, and it throws RPC_E_SERVERFAULT outright when
  any one running application's automation provider misbehaves - which takes down every capture
  script on this machine, for a window none of them care about. Per-window, that application is
  skipped and the rest are still searched.
#>
function Find-ByName {
  param(
    [Parameter(Mandatory)] $ControlType,
    [Parameter(Mandatory)] [string] $Name
  )

  $ua = [System.Windows.Automation.AutomationElement]
  $scope = [System.Windows.Automation.TreeScope]
  $condition = New-Object System.Windows.Automation.PropertyCondition($ua::ControlTypeProperty, $ControlType)

  foreach ($top in $ua::RootElement.FindAll($scope::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
    try {
      foreach ($element in $top.FindAll($scope::Descendants, $condition)) {
        if ($element.Current.Name -like $Name) { return $element }
      }
    } catch {
      continue
    }
  }
  return $null
}

<#
.SYNOPSIS
  Clicks the centre of an element's BoundingRectangle.
.DESCRIPTION
  Deliberately a click rather than InvokePattern.Invoke(): invoking a menu item that opens a modal
  dialog blocks until the dialog closes and then throws a COM timeout, because the dialog's message
  loop never returns to the caller. The dialog does open, so the exception is pure noise.
#>
function Click-Element {
  param(
    [Parameter(Mandatory)] $Element,
    [ValidateSet('left', 'right')] [string] $Button = 'left'
  )

  $r = $Element.Current.BoundingRectangle
  [UiInput]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2)) | Out-Null
  Start-Sleep -Milliseconds 400
  if ($Button -eq 'right') {
    [UiInput]::mouse_event([UiInput]::RIGHT_DOWN, 0, 0, 0, [IntPtr]::Zero)
    [UiInput]::mouse_event([UiInput]::RIGHT_UP, 0, 0, 0, [IntPtr]::Zero)
  } else {
    [UiInput]::mouse_event([UiInput]::LEFT_DOWN, 0, 0, 0, [IntPtr]::Zero)
    [UiInput]::mouse_event([UiInput]::LEFT_UP, 0, 0, 0, [IntPtr]::Zero)
  }
}

<#
.SYNOPSIS
  Right-clicks the tray icon and returns the menu's "Settings..." item once the menu is up.
.DESCRIPTION
  Polls rather than sleeping a fixed interval: a synthetic right-click on the tray opens the menu only
  some of the time, and a script that assumes a delay fails intermittently in a way that looks like a
  different bug each run. Throws when it does not open, so the caller can re-run.
#>
function Open-TrayMenu {
  param([string] $TrayName = "Achievement Overlay", [int] $TimeoutSeconds = 10)

  $tray = Find-ByName -ControlType ([System.Windows.Automation.ControlType]::Button) -Name $TrayName
  if (-not $tray) { throw "Tray icon not found. Is the app running, and is its icon promoted out of the overflow flyout?" }
  Click-Element -Element $tray -Button right

  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
  while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 400
    $item = Find-ByName -ControlType ([System.Windows.Automation.ControlType]::MenuItem) -Name "Settings*"
    if ($item) { return $item }
  }
  throw "Tray menu did not open. Re-run; synthetic right-clicks on the tray are flaky."
}
