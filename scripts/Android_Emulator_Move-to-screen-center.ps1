[CmdletBinding()]
param(
    [string]$AvdName,
    [ValidateRange(0, 300)]
    [int]$WaitSeconds = 30,
    [ValidateRange(0, 200)]
    [int]$Margin = 16,
    [ValidateRange(0, 60)]
    [int]$StabilizeSeconds = 0,
    [switch]$RunInBackground,
    [switch]$IgnoreMissing
)

$ErrorActionPreference = 'Stop'

if (-not $IsWindows) {
    throw 'Android emulator window positioning is supported only on Windows.'
}

if ($RunInBackground) {
    $hostPath = (Get-Process -Id $PID).Path
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $hostPath
    $startInfo.UseShellExecute = $true
    $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    foreach ($argument in @(
            '-NoLogo',
            '-NoProfile',
            '-NonInteractive',
            '-ExecutionPolicy',
            'Bypass',
            '-File',
            $PSCommandPath,
            '-WaitSeconds',
            $WaitSeconds.ToString([Globalization.CultureInfo]::InvariantCulture),
            '-Margin',
            $Margin.ToString([Globalization.CultureInfo]::InvariantCulture),
            '-StabilizeSeconds',
            $StabilizeSeconds.ToString([Globalization.CultureInfo]::InvariantCulture))) {
        [void]$startInfo.ArgumentList.Add($argument)
    }
    if (-not [string]::IsNullOrWhiteSpace($AvdName)) {
        [void]$startInfo.ArgumentList.Add('-AvdName')
        [void]$startInfo.ArgumentList.Add($AvdName)
    }
    if ($IgnoreMissing) {
        [void]$startInfo.ArgumentList.Add('-IgnoreMissing')
    }

    [void][Diagnostics.Process]::Start($startInfo)
    return
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

    $normalWindow = Get-Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.MainWindowHandle -ne [IntPtr]::Zero -and
            $_.MainWindowTitle -match $titlePattern
        } |
        Sort-Object StartTime |
        Select-Object -First 1
    if ($normalWindow) {
        return $normalWindow
    }

    # During an early boot or Windows hang dialog the Android emulator's QEMU
    # window can temporarily be titled only "Emulator". Restrict this fallback
    # to QEMU children from the Android SDK and, when supplied, the exact AVD.
    $androidQemu = Get-CimInstance Win32_Process -Filter "Name = 'qemu-system-x86_64.exe' OR Name = 'qemu-system-aarch64.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.ExecutablePath -match '[\\/]Android[\\/].*[\\/]emulator[\\/]qemu[\\/]' -and
            ([string]::IsNullOrWhiteSpace($AvdName) -or
                $_.CommandLine -match ('(?:^|\s)-avd\s+' + [Regex]::Escape($AvdName) + '(?:\s|$)'))
        } |
        Sort-Object CreationDate

    foreach ($candidate in $androidQemu) {
        $process = Get-Process -Id $candidate.ProcessId -ErrorAction SilentlyContinue
        if ($process -and $process.MainWindowHandle -ne [IntPtr]::Zero) {
            return $process
        }
    }
}

function Set-AndroidEmulatorWindowPosition {
    param([Parameter(Mandatory)]$Window)

    $before = [OtpHarbor.AndroidEmulatorWindow+Rect]::new()
    if (-not [OtpHarbor.AndroidEmulatorWindow]::GetWindowRect($Window.MainWindowHandle, [ref]$before)) {
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
    $asyncWindowPosition = 0x4000
    [void][OtpHarbor.AndroidEmulatorWindow]::ShowWindowAsync($Window.MainWindowHandle, $restoreWindow)
    if (-not [OtpHarbor.AndroidEmulatorWindow]::SetWindowPos(
            $Window.MainWindowHandle,
            [IntPtr]::Zero,
            $targetX,
            $targetY,
            $targetWidth,
            $targetHeight,
            ($noZOrderOrActivation -bor $asyncWindowPosition))) {
        throw "Could not center the Android emulator window (Win32 error $([Runtime.InteropServices.Marshal]::GetLastWin32Error()))."
    }

    [PSCustomObject]@{
        Title = $Window.MainWindowTitle
        PreviousBounds = "{0},{1} {2}x{3}" -f $before.Left, $before.Top, $currentWidth, $currentHeight
        CurrentBounds = "{0},{1} {2}x{3}" -f $targetX, $targetY, $targetWidth, $targetHeight
        WorkingArea = "{0},{1} {2}x{3}" -f $workArea.Left, $workArea.Top, $workArea.Width, $workArea.Height
    }
}

$stopwatch = [Diagnostics.Stopwatch]::StartNew()
$earlyWindowPosition = $null
do {
    $window = Get-AndroidEmulatorWindow
    if ($window) {
        if ($window.MainWindowTitle -match '^Android Emulator(?:\s+-|$)') {
            break
        }

        if (-not $earlyWindowPosition) {
            $earlyWindowPosition = Set-AndroidEmulatorWindowPosition -Window $window
        }
    }

    if ($stopwatch.Elapsed.TotalSeconds -ge $WaitSeconds) {
        if ($window) {
            break
        }

        if ($IgnoreMissing) {
            return
        }

        $suffix = if ([string]::IsNullOrWhiteSpace($AvdName)) { '' } else { " for AVD '$AvdName'" }
        throw "Android emulator window$suffix was not found within $WaitSeconds seconds."
    }

    Start-Sleep -Milliseconds 250
} while ($true)

$position = Set-AndroidEmulatorWindowPosition -Window $window
if ($StabilizeSeconds -gt 0) {
    $stabilization = [Diagnostics.Stopwatch]::StartNew()
    do {
        Start-Sleep -Milliseconds 500
        $refreshedWindow = Get-AndroidEmulatorWindow
        if ($refreshedWindow) {
            $position = Set-AndroidEmulatorWindowPosition -Window $refreshedWindow
        }
    } while ($stabilization.Elapsed.TotalSeconds -lt $StabilizeSeconds)
}

$position
