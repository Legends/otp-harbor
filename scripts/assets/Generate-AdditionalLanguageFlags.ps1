<#
.SYNOPSIS
Generates the additional desktop language-flag PNG assets.

.DESCRIPTION
Draws the reviewed French, German, and Spanish flag bitmaps with System.Drawing and writes them only to the desktop flag asset directory. Refuses output paths outside that directory.
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = 'TOTP.UI.Avalonia.Desktop/Assets/flags'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$resolvedOutput = (Resolve-Path -LiteralPath (Join-Path $repositoryRoot $OutputDirectory)).Path
$expectedOutput = (Resolve-Path -LiteralPath (
    Join-Path $repositoryRoot 'TOTP.UI.Avalonia.Desktop/Assets/flags')).Path
if (-not $resolvedOutput.Equals($expectedOutput, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Language flags may only be generated in the reviewed desktop flag directory.'
}

Add-Type -AssemblyName System.Drawing

function New-FlagBitmap {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][scriptblock]$Draw
    )

    $bitmap = [Drawing.Bitmap]::new(60, 40, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            & $Draw $graphics
        }
        finally {
            $graphics.Dispose()
        }
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

New-FlagBitmap -Path (Join-Path $resolvedOutput 'fr.png') -Draw {
    param($graphics)
    $graphics.Clear([Drawing.Color]::White)
    $graphics.FillRectangle([Drawing.Brushes]::Navy, 0, 0, 20, 40)
    $graphics.FillRectangle([Drawing.Brushes]::Red, 40, 0, 20, 40)
}

New-FlagBitmap -Path (Join-Path $resolvedOutput 'es.png') -Draw {
    param($graphics)
    $red = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#AA151B'))
    $yellow = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#F1BF00'))
    try {
        $graphics.FillRectangle($red, 0, 0, 60, 10)
        $graphics.FillRectangle($yellow, 0, 10, 60, 20)
        $graphics.FillRectangle($red, 0, 30, 60, 10)
    }
    finally {
        $red.Dispose()
        $yellow.Dispose()
    }
}

Write-Output 'Generated reviewed French and Spanish language indicators.'
