# Find the running emulator window
$window = Get-Process |
    Where-Object {
        $_.MainWindowHandle -ne [IntPtr]::Zero -and
        $_.MainWindowTitle -match 'Android Emulator|Pixel.?7'
    } |
    Select-Object -First 1

if (-not $window) {
    throw "Emulator window not found."
}

Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class WindowPosition
{
    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X, int Y,
        int cx, int cy,
        uint uFlags);
}
"@

# Move to x=50, y=50 without resizing
[WindowPosition]::SetWindowPos(
    $window.MainWindowHandle,
    [IntPtr]::Zero,
    50, 50, 0, 0,
    0x0005
)