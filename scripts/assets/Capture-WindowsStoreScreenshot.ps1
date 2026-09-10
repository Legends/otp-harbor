<#
.SYNOPSIS
Captures the running OTP Harbor window for a Microsoft Store listing.

.DESCRIPTION
Resizes and captures only the application window, excluding the desktop and taskbar. The explicit
synthetic-data acknowledgement prevents accidental capture of a real vault.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$WindowName,
    [ValidateRange(1, 4)][int]$Scale = 2,
    [int]$WindowWidth = 0,
    [int]$WindowHeight = 0,
    [switch]$ConfirmSyntheticData
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $ConfirmSyntheticData) {
    throw 'Pass -ConfirmSyntheticData only after verifying that every visible account is synthetic.'
}
if (($WindowWidth -eq 0) -ne ($WindowHeight -eq 0)) {
    throw 'Specify both -WindowWidth and -WindowHeight, or neither.'
}

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class StoreScreenshotNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint flags);
}
'@

$process = Get-Process 'TOTP.UI.Avalonia.Desktop' -ErrorAction Stop |
    Where-Object MainWindowHandle -ne 0 |
    Select-Object -First 1
if ($null -eq $process) {
    throw 'A visible OTP Harbor desktop window is required.'
}

$windowHandle = $process.MainWindowHandle
if (-not [string]::IsNullOrWhiteSpace($WindowName)) {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    $root = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $targetCondition = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new(
            [Windows.Automation.AutomationElement]::NameProperty,
            $WindowName),
        [Windows.Automation.PropertyCondition]::new(
            [Windows.Automation.AutomationElement]::ControlTypeProperty,
            [Windows.Automation.ControlType]::Window))
    $target = $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $targetCondition)
    if ($null -eq $target -or $target.Current.NativeWindowHandle -eq 0) {
        throw "Visible OTP Harbor window was not found: $WindowName"
    }
    $windowHandle = [IntPtr]::new($target.Current.NativeWindowHandle)
}

$originalRect = $null
if ($WindowWidth -gt 0) {
    $originalRect = [StoreScreenshotNative+RECT]::new()
    if (-not [StoreScreenshotNative]::GetWindowRect($windowHandle, [ref]$originalRect)) {
        throw "Unable to measure the original OTP Harbor window. Win32 error: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
    }

    $noMoveNoZOrder = 0x0002 -bor 0x0004
    if (-not [StoreScreenshotNative]::SetWindowPos(
        $windowHandle,
        [IntPtr]::Zero,
        0,
        0,
        $WindowWidth,
        $WindowHeight,
        $noMoveNoZOrder)) {
        throw "Unable to resize the OTP Harbor window. Win32 error: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
    }

    Start-Sleep -Milliseconds 500
}

$rect = [StoreScreenshotNative+RECT]::new()
if (-not [StoreScreenshotNative]::GetWindowRect($windowHandle, [ref]$rect)) {
    throw "Unable to measure the OTP Harbor window. Win32 error: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
}

$captureWidth = $rect.Right - $rect.Left
$captureHeight = $rect.Bottom - $rect.Top
$bitmap = [Drawing.Bitmap]::new(
    $captureWidth,
    $captureHeight,
    [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$deviceContext = $graphics.GetHdc()
try {
    if (-not [StoreScreenshotNative]::PrintWindow($windowHandle, $deviceContext, 2)) {
        throw "Unable to capture the OTP Harbor window. Win32 error: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
    }
}
finally {
    $graphics.ReleaseHdc($deviceContext)
    $graphics.Dispose()
}

try {
    $absoluteOutput = if ([IO.Path]::IsPathRooted($OutputPath)) {
        [IO.Path]::GetFullPath($OutputPath)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputPath))
    }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($absoluteOutput)) | Out-Null
    $outputWidth = $captureWidth * $Scale
    $outputHeight = $captureHeight * $Scale
    if ($outputWidth -lt 1366 -or $outputHeight -lt 768) {
        throw "Store screenshot output is ${outputWidth}x${outputHeight}; increase -Scale to meet the 1366x768 minimum."
    }

    if ($Scale -eq 1) {
        $bitmap.Save($absoluteOutput, [Drawing.Imaging.ImageFormat]::Png)
    }
    else {
        $scaled = [Drawing.Bitmap]::new(
            $outputWidth,
            $outputHeight,
            [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $scaledGraphics = [Drawing.Graphics]::FromImage($scaled)
        try {
            $scaledGraphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
            $scaledGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $scaledGraphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $scaledGraphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
            $scaledGraphics.DrawImage($bitmap, 0, 0, $outputWidth, $outputHeight)
            $scaled.Save($absoluteOutput, [Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $scaledGraphics.Dispose()
            $scaled.Dispose()
        }
    }

    Write-Output "Captured ${outputWidth}x${outputHeight} screenshot: $absoluteOutput"
}
finally {
    $bitmap.Dispose()
}

if ($null -ne $originalRect) {
    $originalWidth = $originalRect.Right - $originalRect.Left
    $originalHeight = $originalRect.Bottom - $originalRect.Top
    if (-not [StoreScreenshotNative]::SetWindowPos(
        $windowHandle,
        [IntPtr]::Zero,
        0,
        0,
        $originalWidth,
        $originalHeight,
        0x0002 -bor 0x0004)) {
        throw "Screenshot was saved, but the original window size could not be restored. Win32 error: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
    }
}
