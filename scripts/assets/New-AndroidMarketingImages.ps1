<#
.SYNOPSIS
Creates the English OTP Harbor Android website campaign from reviewed device captures.

.DESCRIPTION
Composites authentic screenshots from the isolated synthetic-data debug package onto the reviewed
campaign background. The script never reconstructs application or Android system UI.
#>
[CmdletBinding()]
param(
    [string]$BackgroundPath = 'packaging/android/marketing/source/android-marketing-background.png',
    [string]$IconPath = 'TOTP.UI.Avalonia.Desktop/Assets/Icons/app-1024.png',
    [string]$CaptureDirectory = 'packaging/android/marketing/source/captures',
    [string]$OutputDirectory = 'packaging/android/marketing/en-US',
    [string]$PlayStoreOutputDirectory = 'packaging/android/google-play/en-US'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path

function Resolve-RepositoryPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $repositoryRoot $Path))
}

Add-Type -AssemblyName System.Drawing

function New-RoundedPath([Drawing.RectangleF]$Bounds, [float]$Radius) {
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($Bounds.X, $Bounds.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Y, $diameter, $diameter, 270, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Bounds.X, $Bounds.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Draw-Cover(
    [Drawing.Graphics]$Graphics,
    [Drawing.Image]$Image,
    [float]$CanvasWidth = 1920,
    [float]$CanvasHeight = 1080
) {
    $scale = [Math]::Max($CanvasWidth / $Image.Width, $CanvasHeight / $Image.Height)
    $width = $Image.Width * $scale
    $height = $Image.Height * $scale
    $Graphics.DrawImage($Image, [Drawing.RectangleF]::new(
        ($CanvasWidth - $width) / 2, ($CanvasHeight - $height) / 2, $width, $height))
}

function Draw-PhoneCapture(
    [Drawing.Graphics]$Graphics,
    [Drawing.Image]$Capture,
    [Drawing.RectangleF]$Bounds,
    [float]$Radius = 30
) {
    $scale = [Math]::Min($Bounds.Width / $Capture.Width, $Bounds.Height / $Capture.Height)
    $target = [Drawing.RectangleF]::new(
        $Bounds.X + (($Bounds.Width - ($Capture.Width * $scale)) / 2),
        $Bounds.Y + (($Bounds.Height - ($Capture.Height * $scale)) / 2),
        $Capture.Width * $scale,
        $Capture.Height * $scale)
    $shadowBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(90, 0, 0, 0))
    $shadow = New-RoundedPath ([Drawing.RectangleF]::new(
        $target.X + 24, $target.Y + 28, $target.Width, $target.Height)) $Radius
    try { $Graphics.FillPath($shadowBrush, $shadow) }
    finally { $shadow.Dispose(); $shadowBrush.Dispose() }

    $clip = New-RoundedPath $target $Radius
    try {
        $state = $Graphics.Save()
        try { $Graphics.SetClip($clip); $Graphics.DrawImage($Capture, $target) }
        finally { $Graphics.Restore($state) }
        $pen = [Drawing.Pen]::new([Drawing.Color]::FromArgb(210, 87, 205, 255), 3)
        try { $Graphics.DrawPath($pen, $clip) } finally { $pen.Dispose() }
    }
    finally { $clip.Dispose() }
}

function Draw-CampaignCopy(
    [Drawing.Graphics]$Graphics,
    [Drawing.Image]$Icon,
    [string]$Headline,
    [string]$Body,
    [string[]]$Chips,
    [float]$HeadlineSize = 66
) {
    $Graphics.DrawImage($Icon, [Drawing.RectangleF]::new(92, 60, 62, 62))
    $brandFont = [Drawing.Font]::new('Segoe UI Semibold', 22, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $headlineFont = [Drawing.Font]::new('Segoe UI', $HeadlineSize, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $bodyFont = [Drawing.Font]::new('Segoe UI', 30, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
    $chipFont = [Drawing.Font]::new('Segoe UI Semibold', 19, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $white = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 247, 250, 255))
    $muted = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(245, 187, 215, 247))
    try {
        $Graphics.DrawString('OTP HARBOR  /  ANDROID', $brandFont, $white, 176, 77)
        $headlineBounds = [Drawing.RectangleF]::new(92, 188, 760, 330)
        $Graphics.DrawString($Headline, $headlineFont, $white, $headlineBounds)
        $headlineHeight = $Graphics.MeasureString($Headline, $headlineFont, [int]$headlineBounds.Width).Height
        $Graphics.DrawString($Body, $bodyFont, $muted,
            [Drawing.RectangleF]::new(96, [Math]::Min(610, 215 + $headlineHeight), 700, 170))
        $y = 814
        foreach ($chip in $Chips) {
            $size = $Graphics.MeasureString($chip, $chipFont)
            $bounds = [Drawing.RectangleF]::new(96, $y, $size.Width + 42, 48)
            $path = New-RoundedPath $bounds 16
            $fill = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(175, 13, 43, 78))
            $stroke = [Drawing.Pen]::new([Drawing.Color]::FromArgb(220, 70, 205, 255), 2)
            try {
                $Graphics.FillPath($fill, $path)
                $Graphics.DrawPath($stroke, $path)
                $Graphics.DrawString($chip, $chipFont, $white, $bounds.X + 21, $bounds.Y + 10)
            }
            finally { $path.Dispose(); $fill.Dispose(); $stroke.Dispose() }
            $y += 62
        }
    }
    finally {
        $brandFont.Dispose(); $headlineFont.Dispose(); $bodyFont.Dispose(); $chipFont.Dispose()
        $white.Dispose(); $muted.Dispose()
    }
}

function New-CampaignImage(
    [Drawing.Image]$Background,
    [Drawing.Image]$Icon,
    [Drawing.Image]$Primary,
    [Drawing.Image]$Secondary,
    [string]$Headline,
    [string]$Body,
    [string[]]$Chips,
    [string]$Destination,
    [switch]$Pair,
    [float]$HeadlineSize = 66
) {
    $bitmap = [Drawing.Bitmap]::new(1920, 1080, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::ClearTypeGridFit
        Draw-Cover $graphics $Background
        $overlay = [Drawing.Drawing2D.LinearGradientBrush]::new(
            [Drawing.PointF]::new(0, 0), [Drawing.PointF]::new(1500, 0),
            [Drawing.Color]::FromArgb(248, 3, 16, 39), [Drawing.Color]::FromArgb(12, 3, 16, 39))
        try { $graphics.FillRectangle($overlay, 0, 0, 1920, 1080) }
        finally { $overlay.Dispose() }
        Draw-CampaignCopy $graphics $Icon $Headline $Body $Chips $HeadlineSize
        if ($Pair) {
            Draw-PhoneCapture $graphics $Secondary ([Drawing.RectangleF]::new(910, 250, 650, 760)) 24
            Draw-PhoneCapture $graphics $Primary ([Drawing.RectangleF]::new(1270, 58, 560, 960)) 30
        } else {
            Draw-PhoneCapture $graphics $Primary ([Drawing.RectangleF]::new(1010, 50, 810, 980)) 30
        }
        $bitmap.Save($Destination, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
}

function New-PlayStoreScreenshot(
    [Drawing.Image]$Background,
    [Drawing.Image]$Icon,
    [Drawing.Image]$Primary,
    [Drawing.Image]$Secondary,
    [string]$Headline,
    [string]$Body,
    [string]$Destination,
    [switch]$Pair
) {
    $bitmap = [Drawing.Bitmap]::new(1080, 1920, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $white = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 247, 250, 255))
    $muted = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 190, 216, 247))
    $brandFont = [Drawing.Font]::new('Segoe UI Semibold', 28, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $headlineFont = [Drawing.Font]::new('Segoe UI', 64, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $bodyFont = [Drawing.Font]::new('Segoe UI', 31, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
    try {
        $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::ClearTypeGridFit
        Draw-Cover $graphics $Background 1080 1920
        $overlay = [Drawing.Drawing2D.LinearGradientBrush]::new(
            [Drawing.PointF]::new(0, 0), [Drawing.PointF]::new(0, 1500),
            [Drawing.Color]::FromArgb(248, 3, 16, 39), [Drawing.Color]::FromArgb(65, 3, 16, 39))
        try { $graphics.FillRectangle($overlay, 0, 0, 1080, 1920) }
        finally { $overlay.Dispose() }

        $graphics.DrawImage($Icon, [Drawing.RectangleF]::new(64, 54, 64, 64))
        $graphics.DrawString('OTP HARBOR  /  ANDROID', $brandFont, $white, 150, 72)
        $graphics.DrawString($Headline, $headlineFont, $white,
            [Drawing.RectangleF]::new(64, 158, 952, 190))
        $graphics.DrawString($Body, $bodyFont, $muted,
            [Drawing.RectangleF]::new(68, 338, 944, 115))

        if ($Pair) {
            Draw-PhoneCapture $graphics $Secondary ([Drawing.RectangleF]::new(82, 650, 570, 1160)) 26
            Draw-PhoneCapture $graphics $Primary ([Drawing.RectangleF]::new(430, 470, 580, 1320)) 28
        }
        else {
            Draw-PhoneCapture $graphics $Primary ([Drawing.RectangleF]::new(160, 455, 760, 1390)) 30
        }
        $bitmap.Save($Destination, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $brandFont.Dispose(); $headlineFont.Dispose(); $bodyFont.Dispose()
        $white.Dispose(); $muted.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
    }
}

function New-PlayStoreFeatureGraphic(
    [Drawing.Image]$Background,
    [Drawing.Image]$Icon,
    [Drawing.Image]$Primary,
    [Drawing.Image]$Secondary,
    [string]$Destination
) {
    $bitmap = [Drawing.Bitmap]::new(1024, 500, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $white = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 247, 250, 255))
    $muted = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 184, 216, 248))
    $headlineFont = [Drawing.Font]::new('Segoe UI', 61, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $bodyFont = [Drawing.Font]::new('Segoe UI', 27, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
    try {
        $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::ClearTypeGridFit
        Draw-Cover $graphics $Background 1024 500
        $overlay = [Drawing.Drawing2D.LinearGradientBrush]::new(
            [Drawing.PointF]::new(0, 0), [Drawing.PointF]::new(820, 0),
            [Drawing.Color]::FromArgb(252, 3, 16, 39), [Drawing.Color]::FromArgb(20, 3, 16, 39))
        try { $graphics.FillRectangle($overlay, 0, 0, 1024, 500) }
        finally { $overlay.Dispose() }
        $graphics.DrawImage($Icon, [Drawing.RectangleF]::new(116, 56, 82, 82))
        $graphics.DrawString('OTP Harbor', $headlineFont, $white, 116, 164)
        $graphics.DrawString("Local-first TOTP.`nEncrypted on your device.", $bodyFont, $muted,
            [Drawing.RectangleF]::new(120, 252, 450, 110))
        Draw-PhoneCapture $graphics $Secondary ([Drawing.RectangleF]::new(610, 75, 230, 390)) 14
        Draw-PhoneCapture $graphics $Primary ([Drawing.RectangleF]::new(742, 28, 190, 440)) 14
        $bitmap.Save($Destination, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $headlineFont.Dispose(); $bodyFont.Dispose(); $white.Dispose(); $muted.Dispose()
        $graphics.Dispose(); $bitmap.Dispose()
    }
}

function New-PlayStoreIcon([Drawing.Image]$Icon, [string]$Destination) {
    $bitmap = [Drawing.Bitmap]::new(512, 512, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawImage($Icon, [Drawing.RectangleF]::new(0, 0, 512, 512))
        $bitmap.Save($Destination, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
}

$backgroundPath = Resolve-RepositoryPath $BackgroundPath
$iconPath = Resolve-RepositoryPath $IconPath
$capturesPath = Resolve-RepositoryPath $CaptureDirectory
$outputPath = Resolve-RepositoryPath $OutputDirectory
$playStoreOutputPath = Resolve-RepositoryPath $PlayStoreOutputDirectory
$captureFiles = @{
    Vault = Join-Path $capturesPath '01-account-list.jpg'
    Swipe = Join-Path $capturesPath '02-swipe-actions.jpg'
    Qr = Join-Path $capturesPath '03-account-qr.jpg'
    Scanner = Join-Path $capturesPath '04-camera-scanner.png'
    Language = Join-Path $capturesPath '05-language-settings.jpg'
    Security = Join-Path $capturesPath '06-security-settings.jpg'
    ImportExport = Join-Path $capturesPath '07-import-export-settings.jpg'
    Unlock = Join-Path $capturesPath '08-lock-screen.jpg'
}

foreach ($path in @($backgroundPath, $iconPath) + $captureFiles.Values) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required reviewed Android marketing source is missing: $path"
    }
}
[IO.Directory]::CreateDirectory($outputPath) | Out-Null
[IO.Directory]::CreateDirectory($playStoreOutputPath) | Out-Null

$background = [Drawing.Image]::FromFile($backgroundPath)
$icon = [Drawing.Image]::FromFile($iconPath)
$images = @{}
try {
    foreach ($entry in $captureFiles.GetEnumerator()) {
        $images[$entry.Key] = [Drawing.Image]::FromFile($entry.Value)
    }
    New-CampaignImage $background $icon $images.Vault $null `
        "Your codes.`nYour device.`nYour control." `
        'A local encrypted vault for the accounts you use every day.' `
        @('LOCAL FIRST', 'ENCRYPTED VAULT') `
        (Join-Path $outputPath '01-encrypted-local-vault.png')
    New-CampaignImage $background $icon $images.ImportExport $images.Scanner `
        "Scan accounts.`nMove from Google." `
        'Use the camera for account and Google Authenticator transfer QR codes.' `
        @('CAMERA QR IMPORT', 'GOOGLE TRANSFER') `
        (Join-Path $outputPath '02-camera-and-google-qr.png') -Pair -HeadlineSize 62
    New-CampaignImage $background $icon $images.Unlock $images.Security `
        "Fast to unlock.`nProtected by Android." `
        'Choose strong biometrics, Android screen lock, or your master password.' `
        @('THREE UNLOCK CHOICES', 'PASSWORD RECOVERY') `
        (Join-Path $outputPath '03-biometric-quick-unlock.png') -Pair -HeadlineSize 60
    New-CampaignImage $background $icon $images.Swipe $images.Qr `
        "Swipe to manage.`nShow QR deliberately." `
        'Edit, delete or move one synthetic account through clear touch actions.' `
        @('TOUCH-FIRST', 'PER-ACCOUNT QR') `
        (Join-Path $outputPath '04-swipe-manage-show-qr.png') -Pair -HeadlineSize 60
    New-CampaignImage $background $icon $images.Language $images.ImportExport `
        "Portable backups.`nFour languages." `
        'Export an encrypted backup and restore it across supported OTP Harbor platforms.' `
        @('ENCRYPTED BACKUPS', 'ENGLISH + DE + FR + ES') `
        (Join-Path $outputPath '05-backup-and-languages.png') -Pair -HeadlineSize 58

    New-PlayStoreFeatureGraphic $background $icon $images.Vault $images.Security `
        (Join-Path $playStoreOutputPath 'feature-graphic-1024x500.png')
    New-PlayStoreIcon $icon (Join-Path $playStoreOutputPath 'app-icon-512x512.png')
    New-PlayStoreScreenshot $background $icon $images.Vault $null `
        'All your codes.' 'A focused encrypted vault on your Android device.' `
        (Join-Path $playStoreOutputPath '01-local-vault-1080x1920.png')
    New-PlayStoreScreenshot $background $icon $images.Swipe $images.Qr `
        'Swipe to manage.' 'Edit, delete, or show an account QR through deliberate actions.' `
        (Join-Path $playStoreOutputPath '02-swipe-and-qr-1080x1920.png') -Pair
    New-PlayStoreScreenshot $background $icon $images.ImportExport $images.Scanner `
        'Scan and migrate.' 'Import account and Google Authenticator transfer QR codes.' `
        (Join-Path $playStoreOutputPath '03-camera-google-import-1080x1920.png') -Pair
    New-PlayStoreScreenshot $background $icon $images.Unlock $images.Security `
        'Unlock your way.' 'Strong biometrics, Android screen lock, or master password.' `
        (Join-Path $playStoreOutputPath '04-unlock-methods-1080x1920.png') -Pair
    New-PlayStoreScreenshot $background $icon $images.ImportExport $null `
        'Encrypted portability.' 'Export and restore password-protected OTP Harbor backups.' `
        (Join-Path $playStoreOutputPath '05-encrypted-backup-1080x1920.png')
    New-PlayStoreScreenshot $background $icon $images.Language $null `
        'Made for you.' 'Use OTP Harbor in English, German, French, or Spanish.' `
        (Join-Path $playStoreOutputPath '06-four-languages-1080x1920.png')
}
finally {
    foreach ($image in $images.Values) { $image.Dispose() }
    $icon.Dispose(); $background.Dispose()
}

Write-Output "Android website marketing images created in $outputPath"
Write-Output "Android Google Play assets created in $playStoreOutputPath"
