<#
.SYNOPSIS
Builds the Windows and PNG application-icon assets from a source image.

.DESCRIPTION
Resizes the supplied image at high quality, emits the required PNG sizes and multi-resolution ICO file, and uses a disposable temporary working directory that is cleaned after generation.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SourcePath,

    [Parameter(Mandatory)]
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"

$resolvedSource = (Resolve-Path -LiteralPath $SourcePath).Path
$resolvedOutput = (Resolve-Path -LiteralPath $OutputDirectory).Path
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$workDirectory = Join-Path $temporaryRoot ("totp-icon-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $workDirectory | Out-Null

Add-Type -AssemblyName System.Drawing

function New-ResizedPngBytes {
    param(
        [Parameter(Mandatory)]
        [System.Drawing.Image]$Source,

        [Parameter(Mandatory)]
        [int]$Size
    )

    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.DrawImage(
                $Source,
                [System.Drawing.Rectangle]::new(0, 0, $Size, $Size))
        }
        finally {
            $graphics.Dispose()
        }

        $stream = [IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            return ,$stream.ToArray()
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

try {
    $source = [System.Drawing.Image]::FromFile($resolvedSource)
    try {
        $png1024 = New-ResizedPngBytes -Source $source -Size 1024
        $png128 = New-ResizedPngBytes -Source $source -Size 128
        [IO.File]::WriteAllBytes((Join-Path $workDirectory "app-1024.png"), $png1024)
        [IO.File]::WriteAllBytes((Join-Path $workDirectory "app-128.png"), $png128)

        $sizes = @(16, 24, 32, 48, 64, 128, 256)
        $frames = @()
        foreach ($size in $sizes) {
            $frames += ,(New-ResizedPngBytes -Source $source -Size $size)
        }

        $iconPath = Join-Path $workDirectory "app.ico"
        $stream = [IO.File]::Create($iconPath)
        $writer = [IO.BinaryWriter]::new($stream)
        try {
            $writer.Write([uint16]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]$sizes.Count)
            $offset = 6 + (16 * $sizes.Count)
            for ($index = 0; $index -lt $sizes.Count; $index++) {
                $size = $sizes[$index]
                $dimension = if ($size -eq 256) { [byte]0 } else { [byte]$size }
                $writer.Write($dimension)
                $writer.Write($dimension)
                $writer.Write([byte]0)
                $writer.Write([byte]0)
                $writer.Write([uint16]1)
                $writer.Write([uint16]32)
                $writer.Write([uint32]$frames[$index].Length)
                $writer.Write([uint32]$offset)
                $offset += $frames[$index].Length
            }
            foreach ($frame in $frames) {
                $writer.Write($frame)
            }
        }
        finally {
            $writer.Dispose()
        }
    }
    finally {
        $source.Dispose()
    }

    $check1024 = [System.Drawing.Bitmap]::new((Join-Path $workDirectory "app-1024.png"))
    $check128 = [System.Drawing.Bitmap]::new((Join-Path $workDirectory "app-128.png"))
    try {
        if ($check1024.Width -ne 1024 -or
            $check1024.Height -ne 1024 -or
            $check1024.GetPixel(0, 0).A -ne 0) {
            throw "Generated 1024 px PNG failed validation."
        }
        if ($check128.Width -ne 128 -or
            $check128.Height -ne 128 -or
            $check128.GetPixel(0, 0).A -ne 0) {
            throw "Generated 128 px PNG failed validation."
        }
    }
    finally {
        $check1024.Dispose()
        $check128.Dispose()
    }

    $iconBytes = [IO.File]::ReadAllBytes((Join-Path $workDirectory "app.ico"))
    if ($iconBytes.Length -lt 100 -or
        $iconBytes[2] -ne 1 -or
        $iconBytes[4] -ne $sizes.Count) {
        throw "Generated ICO package failed validation."
    }

    Copy-Item -LiteralPath (Join-Path $workDirectory "app-1024.png") -Destination (Join-Path $resolvedOutput "app-1024.png") -Force
    Copy-Item -LiteralPath (Join-Path $workDirectory "app-128.png") -Destination (Join-Path $resolvedOutput "app-128.png") -Force
    Copy-Item -LiteralPath (Join-Path $workDirectory "app.ico") -Destination (Join-Path $resolvedOutput "app.ico") -Force
}
finally {
    $resolvedWorkDirectory = [IO.Path]::GetFullPath($workDirectory)
    if (-not $resolvedWorkDirectory.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove an icon work directory outside the temporary root."
    }
    if (Test-Path -LiteralPath $resolvedWorkDirectory) {
        Remove-Item -LiteralPath $resolvedWorkDirectory -Recurse -Force
    }
}
