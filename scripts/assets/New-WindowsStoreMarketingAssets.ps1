<#
.SYNOPSIS
Builds exact-size Partner Center marketing images from the reviewed abstract background and official icon.

.DESCRIPTION
Creates the Microsoft Store 16:9 super hero, 2:3 poster art, and 300, 150, and 71 pixel square
Store logos. The generated background is a non-secret marketing source asset; the foreground mark
always comes from the canonical repository icon so its geometry and colors remain authentic.
#>
[CmdletBinding()]
param(
    [string]$BackgroundPath = 'packaging/windows-store/assets/source/store-abstract-background.png',
    [string]$IconPath = 'TOTP.UI.Avalonia.Desktop/Assets/Icons/app-1024.png',
    [string]$OutputDirectory = 'packaging/windows-store/assets'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path

function Resolve-RepositoryPath {
    param([Parameter(Mandatory)][string]$Path)

    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }

    return [IO.Path]::GetFullPath((Join-Path $repositoryRoot $Path))
}

Add-Type -AssemblyName System.Drawing

function Draw-CoverImage {
    param(
        [Parameter(Mandatory)][Drawing.Graphics]$Graphics,
        [Parameter(Mandatory)][Drawing.Image]$Image,
        [Parameter(Mandatory)][int]$Width,
        [Parameter(Mandatory)][int]$Height
    )

    $scale = [Math]::Max($Width / $Image.Width, $Height / $Image.Height)
    $drawWidth = [int][Math]::Ceiling($Image.Width * $scale)
    $drawHeight = [int][Math]::Ceiling($Image.Height * $scale)
    $x = [int][Math]::Floor(($Width - $drawWidth) / 2)
    $y = [int][Math]::Floor(($Height - $drawHeight) / 2)
    $Graphics.DrawImage($Image, [Drawing.Rectangle]::new($x, $y, $drawWidth, $drawHeight))
}

function Draw-OfficialIcon {
    param(
        [Parameter(Mandatory)][Drawing.Graphics]$Graphics,
        [Parameter(Mandatory)][Drawing.Image]$Icon,
        [Parameter(Mandatory)][Drawing.Rectangle]$Bounds
    )

    $shadowAttributes = [Drawing.Imaging.ImageAttributes]::new()
    $shadowMatrix = [Drawing.Imaging.ColorMatrix]::new()
    $shadowMatrix.Matrix00 = 0
    $shadowMatrix.Matrix11 = 0
    $shadowMatrix.Matrix22 = 0
    $shadowMatrix.Matrix33 = 0.30
    $shadowAttributes.SetColorMatrix($shadowMatrix)
    try {
        foreach ($offset in 28, 20, 12) {
            $shadow = [Drawing.Rectangle]::new(
                $Bounds.X + $offset,
                $Bounds.Y + $offset,
                $Bounds.Width,
                $Bounds.Height)
            $Graphics.DrawImage(
                $Icon,
                $shadow,
                0,
                0,
                $Icon.Width,
                $Icon.Height,
                [Drawing.GraphicsUnit]::Pixel,
                $shadowAttributes)
        }
    }
    finally {
        $shadowAttributes.Dispose()
    }

    $Graphics.DrawImage($Icon, $Bounds)
}

function Save-MarketingAsset {
    param(
        [Parameter(Mandatory)][Drawing.Image]$Background,
        [Parameter(Mandatory)][Drawing.Image]$Icon,
        [Parameter(Mandatory)][int]$Width,
        [Parameter(Mandatory)][int]$Height,
        [Parameter(Mandatory)][Drawing.Rectangle]$IconBounds,
        [Parameter(Mandatory)][string]$Destination
    )

    $bitmap = [Drawing.Bitmap]::new($Width, $Height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceOver
        $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
        Draw-CoverImage -Graphics $graphics -Image $Background -Width $Width -Height $Height
        Draw-OfficialIcon -Graphics $graphics -Icon $Icon -Bounds $IconBounds
        $bitmap.Save($Destination, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$resolvedBackground = Resolve-RepositoryPath $BackgroundPath
$resolvedIcon = Resolve-RepositoryPath $IconPath
$resolvedOutput = Resolve-RepositoryPath $OutputDirectory

if (-not (Test-Path -LiteralPath $resolvedBackground -PathType Leaf)) {
    throw "Store background is missing: $resolvedBackground"
}
if (-not (Test-Path -LiteralPath $resolvedIcon -PathType Leaf)) {
    throw "Official app icon is missing: $resolvedIcon"
}
[IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

$background = [Drawing.Image]::FromFile($resolvedBackground)
$icon = [Drawing.Image]::FromFile($resolvedIcon)
try {
    Save-MarketingAsset `
        -Background $background `
        -Icon $icon `
        -Width 1920 `
        -Height 1080 `
        -IconBounds ([Drawing.Rectangle]::new(700, 220, 520, 520)) `
        -Destination (Join-Path $resolvedOutput 'store-super-hero-1920x1080.png')

    Save-MarketingAsset `
        -Background $background `
        -Icon $icon `
        -Width 720 `
        -Height 1080 `
        -IconBounds ([Drawing.Rectangle]::new(145, 245, 430, 430)) `
        -Destination (Join-Path $resolvedOutput 'store-poster-art-720x1080.png')

    Save-MarketingAsset `
        -Background $background `
        -Icon $icon `
        -Width 300 `
        -Height 300 `
        -IconBounds ([Drawing.Rectangle]::new(48, 48, 204, 204)) `
        -Destination (Join-Path $resolvedOutput 'store-app-tile-300x300.png')

    Save-MarketingAsset `
        -Background $background `
        -Icon $icon `
        -Width 150 `
        -Height 150 `
        -IconBounds ([Drawing.Rectangle]::new(24, 24, 102, 102)) `
        -Destination (Join-Path $resolvedOutput 'store-logo-150x150.png')

    Save-MarketingAsset `
        -Background $background `
        -Icon $icon `
        -Width 71 `
        -Height 71 `
        -IconBounds ([Drawing.Rectangle]::new(11, 11, 49, 49)) `
        -Destination (Join-Path $resolvedOutput 'store-logo-71x71.png')
}
finally {
    $icon.Dispose()
    $background.Dispose()
}

Write-Output "Microsoft Store marketing assets created in $resolvedOutput"
