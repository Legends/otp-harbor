[CmdletBinding()]
param(
    [string]$AvdName,
    [ValidateRange(0, 300)]
    [int]$WaitSeconds = 30,
    [ValidateRange(0, 200)]
    [int]$Margin = 16,
    [switch]$IgnoreMissing
)

$ErrorActionPreference = 'Stop'

if (-not $IsWindows) {
    throw 'Android emulator window positioning is supported only on Windows.'
}

Add-Type -AssemblyName System.Windows.Forms

if (-not ('OtpHarbor.AndroidEmulatorWindow' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace OtpHarbor
{
    public static class AndroidEmulatorWindow
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr window, out Rect rect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(
            IntPtr window,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindowAsync(IntPtr window, int command);
    }
}
'@
}

function Get-AndroidEmulatorWindow {
    $titlePattern = if ([string]::IsNullOrWhiteSpace($AvdName)) {
        '^Android Emulator(?:\s+-|$)'
    }
    else {
        '^Android Emulator\s+-\s+' + [Regex]::Escape($AvdName) + '(?::\d+)?$'
    }

    Get-Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.MainWindowHandle -ne [IntPtr]::Zero -and
            $_.MainWindowTitle -match $titlePattern
        } |
        Sort-Object StartTime |
        Select-Object -First 1
}

$stopwatch = [Diagnostics.Stopwatch]::StartNew()
do {
    $window = Get-AndroidEmulatorWindow
    if ($window) {
        break
    }

    if ($stopwatch.Elapsed.TotalSeconds -ge $WaitSeconds) {
        if ($IgnoreMissing) {
            return
        }

        $suffix = if ([string]::IsNullOrWhiteSpace($AvdName)) { '' } else { " for AVD '$AvdName'" }
        throw "Android emulator window$suffix was not found within $WaitSeconds seconds."
    }

    Start-Sleep -Milliseconds 250
} while ($true)

$before = [OtpHarbor.AndroidEmulatorWindow+Rect]::new()
if (-not [OtpHarbor.AndroidEmulatorWindow]::GetWindowRect($window.MainWindowHandle, [ref]$before)) {
    throw "Could not read the Android emulator window bounds (Win32 error $([Runtime.InteropServices.Marshal]::GetLastWin32Error()))."
}

$workArea = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$availableWidth = [Math]::Max(1, $workArea.Width - (2 * $Margin))
$availableHeight = [Math]::Max(1, $workArea.Height - (2 * $Margin))
$currentWidth = [Math]::Max(1, $before.Right - $before.Left)
$currentHeight = [Math]::Max(1, $before.Bottom - $before.Top)
$scale = [Math]::Min(1.0, [Math]::Min($availableWidth / $currentWidth, $availableHeight / $currentHeight))
$targetWidth = [Math]::Max(1, [int][Math]::Floor($currentWidth * $scale))
$targetHeight = [Math]::Max(1, [int][Math]::Floor($currentHeight * $scale))
$targetX = $workArea.Left + [int][Math]::Floor(($workArea.Width - $targetWidth) / 2)
$targetY = $workArea.Top + [int][Math]::Floor(($workArea.Height - $targetHeight) / 2)

$restoreWindow = 9
$noZOrderOrActivation = 0x0014
[void][OtpHarbor.AndroidEmulatorWindow]::ShowWindowAsync($window.MainWindowHandle, $restoreWindow)
if (-not [OtpHarbor.AndroidEmulatorWindow]::SetWindowPos(
        $window.MainWindowHandle,
        [IntPtr]::Zero,
        $targetX,
        $targetY,
        $targetWidth,
        $targetHeight,
        $noZOrderOrActivation)) {
    throw "Could not center the Android emulator window (Win32 error $([Runtime.InteropServices.Marshal]::GetLastWin32Error()))."
}

[PSCustomObject]@{
    Title = $window.MainWindowTitle
    PreviousBounds = "{0},{1} {2}x{3}" -f $before.Left, $before.Top, $currentWidth, $currentHeight
    CurrentBounds = "{0},{1} {2}x{3}" -f $targetX, $targetY, $targetWidth, $targetHeight
    WorkingArea = "{0},{1} {2}x{3}" -f $workArea.Left, $workArea.Top, $workArea.Width, $workArea.Height
}
