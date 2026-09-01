# Captures a window to a PNG with a real alpha channel, so whatever sits behind it on screen is
# transparent rather than baked in.
#
# Dot-source this and call Save-WindowCapture.
#
# Why not just crop: a window's rounded corners are antialiased, so the boundary pixels are a *blend*
# of window and desktop - cropping either keeps grey fringes or eats content. And the achievement
# popup is genuinely translucent (#DD fill over the game), so there is no crop that separates it from
# the wallpaper at all. Both need the background removed, not trimmed.
#
# How: photograph the window twice over known backdrops, then solve for what it actually is. For a
# pixel of colour C and coverage a over a backdrop B, the screen shows  O = C*a + B*(1-a).
# Over black (B=0):  O_k = C*a
# Over white (B=255): O_w = C*a + 255*(1-a)
# so  a = 1 - (O_w - O_k)/255  and  C = O_k / a. Exact for any partial coverage, which is what makes
# it work on antialiased curves and on translucent fills alike.

Add-Type -AssemblyName System.Drawing, System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class WinCap {
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

  public const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010;

  public static RECT ClientBounds(IntPtr h) {
    RECT c; GetClientRect(h, out c);
    POINT p; p.X = c.Left; p.Y = c.Top; ClientToScreen(h, ref p);
    RECT r; r.Left = p.X; r.Top = p.Y;
    r.Right = p.X + (c.Right - c.Left); r.Bottom = p.Y + (c.Bottom - c.Top);
    return r;
  }

  // Solves O = C*a + B*(1-a) per pixel from the black-backdrop and white-backdrop captures.
  // In C# rather than PowerShell because this runs per pixel and a window is a couple of million of
  // them; the same loop in script takes the better part of a minute.
  public static byte[] Unmix(byte[] k, byte[] w) {
    var o = new byte[k.Length];
    for (int i = 0; i < k.Length; i += 4) {
      // BGRA. The three channels should agree on alpha; average them to shrug off rounding.
      int d = ((w[i] - k[i]) + (w[i + 1] - k[i + 1]) + (w[i + 2] - k[i + 2])) / 3;
      int a = 255 - d;
      if (a <= 0) { o[i] = o[i + 1] = o[i + 2] = o[i + 3] = 0; continue; }
      if (a > 255) a = 255;
      o[i]     = (byte)Math.Min(255, k[i]     * 255 / a);
      o[i + 1] = (byte)Math.Min(255, k[i + 1] * 255 / a);
      o[i + 2] = (byte)Math.Min(255, k[i + 2] * 255 / a);
      o[i + 3] = (byte)a;
    }
    return o;
  }
}
"@

function New-Backdrop {
  param([System.Drawing.Rectangle] $Bounds, [IntPtr] $Below)

  $form = New-Object System.Windows.Forms.Form
  $form.FormBorderStyle = 'None'
  $form.ShowInTaskbar = $false
  $form.StartPosition = 'Manual'
  $form.Bounds = $Bounds
  $form.BackColor = [System.Drawing.Color]::Black
  $form.Show()
  # Slot it directly beneath the target rather than making anything topmost, so the window under
  # inspection keeps whatever z-order and activation it already had.
  [WinCap]::SetWindowPos($form.Handle, $Below, 0, 0, 0, 0,
    ([WinCap]::SWP_NOMOVE -bor [WinCap]::SWP_NOSIZE -bor [WinCap]::SWP_NOACTIVATE)) | Out-Null
  [WinCap]::SetForegroundWindow($Below) | Out-Null
  return $form
}

function Get-Shot {
  param([System.Drawing.Rectangle] $Bounds)

  $bmp = New-Object System.Drawing.Bitmap $Bounds.Width, $Bounds.Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($Bounds.X, $Bounds.Y, 0, 0, $bmp.Size)
  $g.Dispose()
  return $bmp
}

function Get-Bytes {
  param([System.Drawing.Bitmap] $Bitmap)

  $rect = New-Object System.Drawing.Rectangle 0, 0, $Bitmap.Width, $Bitmap.Height
  $data = $Bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $buf = New-Object byte[] ($data.Stride * $Bitmap.Height)
  [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)
  $Bitmap.UnlockBits($data)
  return $buf
}

<#
.SYNOPSIS
  Saves a window as a PNG whose background is transparent rather than whatever was behind it.
.PARAMETER Hwnd
  The window to capture.
.PARAMETER Path
  Where to write the PNG.
.PARAMETER WholeWindow
  Capture the whole window instead of just its client area. The client area is the default because it
  excludes the title bar and Windows 11's own border.
.PARAMETER CropToOpaque
  Shrink the result until all four corners are fully opaque. For an opaque window this removes the
  bottom corners, where Windows 11's rounded frame curves *into* the client rectangle and leaves a
  half-covered arc of the window's own dark border. That arc is chrome, not background, so recovering
  alpha preserves it rather than removing it - the only way to be rid of it is to not include it.
  Leave this off for a window that is translucent by design, or it will eat the whole subject.
#>
function Save-WindowCapture {
  param(
    [Parameter(Mandatory)] [IntPtr] $Hwnd,
    [Parameter(Mandatory)] [string] $Path,
    [switch] $WholeWindow,
    [switch] $CropToOpaque
  )

  $r = if ($WholeWindow) {
    $wr = New-Object "WinCap+RECT"; [WinCap]::GetWindowRect($Hwnd, [ref] $wr) | Out-Null; $wr
  } else { [WinCap]::ClientBounds($Hwnd) }

  $bounds = New-Object System.Drawing.Rectangle $r.Left, $r.Top, ($r.Right - $r.Left), ($r.Bottom - $r.Top)
  # The backdrop has to cover the rounded corners, which spill a few pixels outside the client area.
  $pad = 16
  $backdropBounds = New-Object System.Drawing.Rectangle ($bounds.X - $pad), ($bounds.Y - $pad), ($bounds.Width + 2 * $pad), ($bounds.Height + 2 * $pad)

  $backdrop = New-Backdrop -Bounds $backdropBounds -Below $Hwnd
  try {
    Start-Sleep -Milliseconds 500
    $onBlack = Get-Shot -Bounds $bounds

    $backdrop.BackColor = [System.Drawing.Color]::White
    $backdrop.Refresh()
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 500
    $onWhite = Get-Shot -Bounds $bounds
  } finally {
    $backdrop.Close(); $backdrop.Dispose()
  }

  $merged = [WinCap]::Unmix((Get-Bytes $onBlack), (Get-Bytes $onWhite))

  $out = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $rect = New-Object System.Drawing.Rectangle 0, 0, $out.Width, $out.Height
  $data = $out.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  [System.Runtime.InteropServices.Marshal]::Copy($merged, 0, $data.Scan0, $merged.Length)
  $out.UnlockBits($data)

  $inset = 0
  if ($CropToOpaque) {
    # Grow the inset until every corner is solid. Measured per capture rather than hardcoded, so it
    # follows the OS corner radius and the display scaling instead of assuming this machine's.
    while ($inset -lt 32) {
      $x1 = $out.Width - 1 - $inset; $y1 = $out.Height - 1 - $inset
      $corners = @($out.GetPixel($inset, $inset), $out.GetPixel($x1, $inset),
                   $out.GetPixel($inset, $y1),    $out.GetPixel($x1, $y1))
      if (-not ($corners | Where-Object { $_.A -lt 255 })) { break }
      $inset++
    }
    if ($inset -gt 0) {
      $keep = New-Object System.Drawing.Rectangle $inset, $inset, ($out.Width - 2 * $inset), ($out.Height - 2 * $inset)
      $cropped = $out.Clone($keep, $out.PixelFormat)
      $out.Dispose(); $out = $cropped
    }
  }

  $out.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)

  $w = $out.Width; $h = $out.Height
  $onBlack.Dispose(); $onWhite.Dispose(); $out.Dispose()
  $note = if ($inset -gt 0) { " (inset ${inset}px to clear the rounded corners)" } else { "" }
  Write-Output "captured ${w}x${h} with alpha${note}"
}
