<#
.SYNOPSIS
Creates the German Microsoft Store marketing screenshot set from reviewed project assets.

.DESCRIPTION
Composites authentic OTP Harbor captures containing synthetic accounts onto the reviewed campaign
background. Text and UI captures are rendered deterministically so generated artwork cannot alter
product claims, account data, or interface details.
#>
[CmdletBinding()]
param(
    [string]$BackgroundPath = 'packaging/windows-store/assets/source/store-marketing-screenshot-background.png',
    [string]$IconPath = 'TOTP.UI.Avalonia.Desktop/Assets/Icons/app-1024.png',
    [string]$ScreenshotDirectory = 'packaging/windows-store/screenshots/en-US',
    [string]$WindowsHelloPath = 'packaging/windows-store/assets/source/windows-hello-quick-unlock.png',
    [string]$OutputDirectory = 'packaging/windows-store/screenshots/de-DE/marketing'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path

function Resolve-RepositoryPath {
    param([Parameter(Mandatory)][string]$Path)
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $repositoryRoot $Path))
}

Add-Type -AssemblyName System.Drawing

function New-RoundedPath {
    param([Parameter(Mandatory)][Drawing.RectangleF]$Bounds, [float]$Radius = 26)
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($Bounds.X, $Bounds.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Y, $diameter, $diameter, 270, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Bounds.X, $Bounds.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Draw-Cover {
    param([Drawing.Graphics]$Graphics, [Drawing.Image]$Image, [Drawing.RectangleF]$Bounds)
    $scale = [Math]::Max($Bounds.Width / $Image.Width, $Bounds.Height / $Image.Height)
    $width = $Image.Width * $scale
    $height = $Image.Height * $scale
    $Graphics.DrawImage($Image, [Drawing.RectangleF]::new(
        $Bounds.X + (($Bounds.Width - $width) / 2),
        $Bounds.Y + (($Bounds.Height - $height) / 2),
        $width,
        $height))
}

function Draw-ContainedImage {
    param(
        [Drawing.Graphics]$Graphics,
        [Drawing.Image]$Image,
        [Drawing.RectangleF]$Bounds,
        [switch]$Frame
    )
    $scale = [Math]::Min($Bounds.Width / $Image.Width, $Bounds.Height / $Image.Height)
    $width = $Image.Width * $scale
    $height = $Image.Height * $scale
    $target = [Drawing.RectangleF]::new(
        $Bounds.X + (($Bounds.Width - $width) / 2),
        $Bounds.Y + (($Bounds.Height - $height) / 2),
        $width,
        $height)

    if ($Frame) {
        foreach ($offset in 28, 18, 10) {
            $shadowBounds = [Drawing.RectangleF]::new(
                $target.X + $offset,
                $target.Y + $offset,
                $target.Width,
                $target.Height)
            $shadowPath = New-RoundedPath -Bounds $shadowBounds -Radius 30
            try {
                $shadowBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(35, 0, 0, 0))
                try { $Graphics.FillPath($shadowBrush, $shadowPath) } finally { $shadowBrush.Dispose() }
            }
            finally { $shadowPath.Dispose() }
        }
    }

    $clipPath = New-RoundedPath -Bounds $target -Radius 24
    try {
        $state = $Graphics.Save()
        try {
            $Graphics.SetClip($clipPath)
            $Graphics.DrawImage($Image, $target)
        }
        finally { $Graphics.Restore($state) }
        $borderPen = [Drawing.Pen]::new([Drawing.Color]::FromArgb(150, 102, 210, 255), 3)
        try { $Graphics.DrawPath($borderPen, $clipPath) } finally { $borderPen.Dispose() }
    }
    finally { $clipPath.Dispose() }
}

function Draw-BrandHeader {
    param([Drawing.Graphics]$Graphics, [Drawing.Image]$Icon)
    $Graphics.DrawImage($Icon, [Drawing.RectangleF]::new(96, 62, 62, 62))
    $font = [Drawing.Font]::new('Segoe UI Semibold', 22, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $brush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(235, 226, 239, 255))
    try { $Graphics.DrawString('OTP HARBOR  /  WINDOWS', $font, $brush, 178, 78) }
    finally { $brush.Dispose(); $font.Dispose() }
}

function Draw-Copy {
    param(
        [Drawing.Graphics]$Graphics,
        [string]$Headline,
        [string]$Body,
        [string[]]$Chips,
        [float]$HeadlineSize = 70
    )
    $headlineFont = [Drawing.Font]::new('Segoe UI', $HeadlineSize, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $bodyFont = [Drawing.Font]::new('Segoe UI', 31, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
    $chipFont = [Drawing.Font]::new('Segoe UI Semibold', 20, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $white = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 245, 249, 255))
    $muted = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(235, 184, 211, 245))
    try {
        $headlineBounds = [Drawing.RectangleF]::new(96, 182, 790, 360)
        $Graphics.DrawString($Headline, $headlineFont, $white, $headlineBounds)
        $headlineHeight = $Graphics.MeasureString($Headline, $headlineFont, [int]$headlineBounds.Width).Height
        $bodyY = [Math]::Min(600, 206 + $headlineHeight)
        $Graphics.DrawString($Body, $bodyFont, $muted, [Drawing.RectangleF]::new(100, $bodyY, 730, 150))

        $chipY = 805
        foreach ($chip in $Chips) {
            $size = $Graphics.MeasureString($chip, $chipFont)
            $bounds = [Drawing.RectangleF]::new(100, $chipY, $size.Width + 42, 48)
            $path = New-RoundedPath -Bounds $bounds -Radius 16
            try {
                $fill = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(150, 20, 54, 94))
                $stroke = [Drawing.Pen]::new([Drawing.Color]::FromArgb(180, 61, 197, 255), 2)
                try {
                    $Graphics.FillPath($fill, $path)
                    $Graphics.DrawPath($stroke, $path)
                    $Graphics.DrawString($chip, $chipFont, $white, $bounds.X + 21, $bounds.Y + 10)
                }
                finally { $fill.Dispose(); $stroke.Dispose() }
            }
            finally { $path.Dispose() }
            $chipY += 62
        }
    }
    finally {
        $white.Dispose(); $muted.Dispose()
        $headlineFont.Dispose(); $bodyFont.Dispose(); $chipFont.Dispose()
    }
}

function New-MarketingScreenshot {
    param(
        [Drawing.Image]$Background,
        [Drawing.Image]$Icon,
        [Drawing.Image]$Screenshot,
        [string]$Headline,
        [string]$Body,
        [string[]]$Chips,
        [string]$Destination,
        [switch]$TransparentScreenshot,
        [float]$HeadlineSize = 70
    )
    $bitmap = [Drawing.Bitmap]::new(1920, 1080, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::ClearTypeGridFit

        Draw-Cover -Graphics $graphics -Image $Background -Bounds ([Drawing.RectangleF]::new(0, 0, 1920, 1080))
        $overlay = [Drawing.Drawing2D.LinearGradientBrush]::new(
            [Drawing.PointF]::new(0, 0),
            [Drawing.PointF]::new(1920, 0),
            [Drawing.Color]::FromArgb(245, 4, 16, 39),
            [Drawing.Color]::FromArgb(0, 4, 16, 39))
        try { $graphics.FillRectangle($overlay, 0, 0, 1920, 1080) }
        finally { $overlay.Dispose() }

        Draw-BrandHeader -Graphics $graphics -Icon $Icon
        Draw-Copy -Graphics $graphics -Headline $Headline -Body $Body -Chips $Chips -HeadlineSize $HeadlineSize

        $screenBounds = if ($TransparentScreenshot) {
            [Drawing.RectangleF]::new(930, 80, 930, 950)
        }
        else {
            [Drawing.RectangleF]::new(1030, 92, 800, 910)
        }
        Draw-ContainedImage -Graphics $graphics -Image $Screenshot -Bounds $screenBounds -Frame:(-not $TransparentScreenshot)

        $bitmap.Save($Destination, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
}

$resolvedBackground = Resolve-RepositoryPath $BackgroundPath
$resolvedIcon = Resolve-RepositoryPath $IconPath
$resolvedScreenshots = Resolve-RepositoryPath $ScreenshotDirectory
$resolvedHello = Resolve-RepositoryPath $WindowsHelloPath
$resolvedOutput = Resolve-RepositoryPath $OutputDirectory
[IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

$required = @(
    $resolvedBackground,
    $resolvedIcon,
    (Join-Path $resolvedScreenshots '01-account-dashboard.png'),
    (Join-Path $resolvedScreenshots '02-search-accounts.png'),
    (Join-Path $resolvedScreenshots '03-add-account.png'),
    (Join-Path $resolvedScreenshots '04-quick-unlock.png'),
    $resolvedHello
)
foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required marketing source is missing: $path" }
}

$background = [Drawing.Image]::FromFile($resolvedBackground)
$icon = [Drawing.Image]::FromFile($resolvedIcon)
$dashboard = [Drawing.Image]::FromFile((Join-Path $resolvedScreenshots '01-account-dashboard.png'))
$search = [Drawing.Image]::FromFile((Join-Path $resolvedScreenshots '02-search-accounts.png'))
$add = [Drawing.Image]::FromFile((Join-Path $resolvedScreenshots '03-add-account.png'))
$locked = [Drawing.Image]::FromFile((Join-Path $resolvedScreenshots '04-quick-unlock.png'))
$hello = [Drawing.Image]::FromFile($resolvedHello)
try {
    New-MarketingScreenshot -Background $background -Icon $icon -Screenshot $dashboard `
        -Headline "Deine Codes.`nDein Gerät.`nDeine Kontrolle." `
        -Body 'Ein lokaler, verschlüsselter TOTP-Tresor – ganz ohne Cloudkonto.' `
        -Chips @('LOKAL GESPEICHERT', 'VERSCHLÜSSELT') `
        -Destination (Join-Path $resolvedOutput '01-lokaler-tresor.png') -HeadlineSize 66

    New-MarketingScreenshot -Background $background -Icon $icon -Screenshot $search `
        -Headline "Finden. Kopieren.`nWeiterarbeiten." `
        -Body 'Durchsuche deine Konten sofort und kopiere den aktuellen Code mit einem Klick.' `
        -Chips @('SCHNELLE SUCHE', 'LIVE-CODES') `
        -Destination (Join-Path $resolvedOutput '02-suchen-und-kopieren.png')

    New-MarketingScreenshot -Background $background -Icon $icon -Screenshot $add `
        -Headline "Konten schnell`nhinzufügen." `
        -Body 'Manuell oder per QR-Code – mit flexiblen TOTP-Einstellungen.' `
        -Chips @('QR-IMPORT', 'MANUELLE EINGABE') `
        -Destination (Join-Path $resolvedOutput '03-konten-hinzufuegen.png')

    New-MarketingScreenshot -Background $background -Icon $icon -Screenshot $hello `
        -Headline "Schnell entsperrt.`nSicher geschützt." `
        -Body 'Windows Hello für den Alltag. Das Masterpasswort bleibt deine Wiederherstellung.' `
        -Chips @('WINDOWS HELLO', 'QUICK UNLOCK') `
        -Destination (Join-Path $resolvedOutput '04-windows-hello.png') -TransparentScreenshot

    New-MarketingScreenshot -Background $background -Icon $icon -Screenshot $locked `
        -Headline "Geschützt, wenn`nes darauf ankommt." `
        -Body 'Automatische Sperre, verschlüsselte Backups und kontrollierte Wiederherstellung.' `
        -Chips @('AUTO-LOCK', 'BACKUP & RESTORE') `
        -Destination (Join-Path $resolvedOutput '05-sperre-und-backup.png')
}
finally {
    $hello.Dispose(); $locked.Dispose(); $add.Dispose(); $search.Dispose(); $dashboard.Dispose()
    $icon.Dispose(); $background.Dispose()
}

Write-Output "German Store marketing screenshots created in $resolvedOutput"
