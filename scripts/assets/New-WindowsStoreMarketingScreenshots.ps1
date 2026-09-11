<#
.SYNOPSIS
Creates localized Microsoft Store marketing screenshot sets from reviewed project assets.

.DESCRIPTION
Composites authentic OTP Harbor captures containing synthetic accounts onto the reviewed campaign
background. Localized text and UI captures are rendered deterministically so generated artwork
cannot alter product claims, account data, or interface details.
#>
[CmdletBinding()]
param(
    [string]$BackgroundPath = 'packaging/windows-store/assets/source/store-marketing-screenshot-background.png',
    [string]$IconPath = 'TOTP.UI.Avalonia.Desktop/Assets/Icons/app-1024.png',
    [string]$ScreenshotDirectory = 'packaging/windows-store/screenshots/en-US',
    [string]$WindowsHelloPromptPath = 'packaging/windows-store/assets/source/windows-hello-prompt.png',
    [string]$QuickUnlockPath = 'packaging/windows-store/assets/source/quick-unlock-screen.png',
    [string]$ImportExportPath = 'packaging/windows-store/assets/source/import-export-settings.png',
    [string]$ImportQrPath = 'packaging/windows-store/assets/source/import-qr-camera.png',
    [ValidateSet('en-US', 'de-DE', 'fr-FR', 'es-ES')]
    [string[]]$Cultures = @('en-US', 'de-DE', 'fr-FR', 'es-ES'),
    [string]$OutputRoot = 'packaging/windows-store/screenshots'
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
        [Drawing.Image]$SecondaryScreenshot,
        [string]$Headline,
        [string]$Body,
        [string[]]$Chips,
        [string]$Destination,
        [ValidateSet('Standard', 'HelloPair', 'ImportExportPair')]
        [string]$Layout = 'Standard',
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

        switch ($Layout) {
            'HelloPair' {
                if ($null -eq $SecondaryScreenshot) { throw 'HelloPair requires a secondary screenshot.' }
                Draw-ContainedImage -Graphics $graphics -Image $Screenshot `
                    -Bounds ([Drawing.RectangleF]::new(1270, 80, 540, 920)) -Frame
                Draw-ContainedImage -Graphics $graphics -Image $SecondaryScreenshot `
                    -Bounds ([Drawing.RectangleF]::new(880, 250, 820, 760)) -Frame
            }
            'ImportExportPair' {
                if ($null -eq $SecondaryScreenshot) { throw 'ImportExportPair requires a secondary screenshot.' }
                Draw-ContainedImage -Graphics $graphics -Image $Screenshot `
                    -Bounds ([Drawing.RectangleF]::new(1210, 70, 610, 900)) -Frame
                Draw-ContainedImage -Graphics $graphics -Image $SecondaryScreenshot `
                    -Bounds ([Drawing.RectangleF]::new(900, 350, 890, 640)) -Frame
            }
            default {
                Draw-ContainedImage -Graphics $graphics -Image $Screenshot `
                    -Bounds ([Drawing.RectangleF]::new(1030, 92, 800, 910)) -Frame
            }
        }

        $bitmap.Save($Destination, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
}

$resolvedBackground = Resolve-RepositoryPath $BackgroundPath
$resolvedIcon = Resolve-RepositoryPath $IconPath
$resolvedScreenshots = Resolve-RepositoryPath $ScreenshotDirectory
$resolvedHelloPrompt = Resolve-RepositoryPath $WindowsHelloPromptPath
$resolvedQuickUnlock = Resolve-RepositoryPath $QuickUnlockPath
$resolvedImportExport = Resolve-RepositoryPath $ImportExportPath
$resolvedImportQr = Resolve-RepositoryPath $ImportQrPath
$resolvedOutputRoot = Resolve-RepositoryPath $OutputRoot

$required = @(
    $resolvedBackground,
    $resolvedIcon,
    (Join-Path $resolvedScreenshots '01-account-dashboard.png'),
    (Join-Path $resolvedScreenshots '02-search-accounts.png'),
    (Join-Path $resolvedScreenshots '03-add-account.png'),
    $resolvedHelloPrompt,
    $resolvedQuickUnlock,
    $resolvedImportExport,
    $resolvedImportQr
)
foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required marketing source is missing: $path" }
}

$background = [Drawing.Image]::FromFile($resolvedBackground)
$icon = [Drawing.Image]::FromFile($resolvedIcon)
$dashboard = [Drawing.Image]::FromFile((Join-Path $resolvedScreenshots '01-account-dashboard.png'))
$search = [Drawing.Image]::FromFile((Join-Path $resolvedScreenshots '02-search-accounts.png'))
$add = [Drawing.Image]::FromFile((Join-Path $resolvedScreenshots '03-add-account.png'))
$helloPrompt = [Drawing.Image]::FromFile($resolvedHelloPrompt)
$quickUnlock = [Drawing.Image]::FromFile($resolvedQuickUnlock)
$importExport = [Drawing.Image]::FromFile($resolvedImportExport)
$importQr = [Drawing.Image]::FromFile($resolvedImportQr)
try {
    $localizedCampaigns = @{
        'en-US' = @(
            @{ File = '01-local-vault.png'; Screenshot = $dashboard; Headline = "Your codes.`nYour device.`nYour control."; Body = 'A local, encrypted TOTP vault — with no cloud account.'; Chips = @('LOCAL FIRST', 'ENCRYPTED'); Size = 66 }
            @{ File = '02-search-and-copy.png'; Screenshot = $search; Headline = "Find. Copy.`nKeep moving."; Body = 'Search accounts instantly and copy the current code with one click.'; Chips = @('QUICK SEARCH', 'LIVE CODES'); Size = 70 }
            @{ File = '03-add-and-import.png'; Screenshot = $add; Headline = "Add accounts`nin seconds."; Body = 'Enter details manually or import a QR code — with flexible TOTP settings.'; Chips = @('QR IMPORT', 'MANUAL ENTRY'); Size = 70 }
            @{ File = '04-windows-hello.png'; Screenshot = $quickUnlock; Secondary = $helloPrompt; Layout = 'HelloPair'; Headline = "Fast to unlock.`nSecure by design."; Body = 'Windows Hello for everyday access. Your master password remains the recovery method.'; Chips = @('WINDOWS HELLO', 'QUICK UNLOCK'); Size = 68 }
            @{ File = '05-import-export.png'; Screenshot = $importExport; Secondary = $importQr; Layout = 'ImportExportPair'; Headline = "Import. Export.`nMove with confidence."; Body = 'Import and export encrypted backups, import Google Authenticator QR exports, or scan single-account codes with your camera.'; Chips = @('GOOGLE QR IMPORT', 'CAMERA OR FILE'); Size = 62 }
        )
        'de-DE' = @(
            @{ File = '01-local-vault.png'; Screenshot = $dashboard; Headline = "Deine Codes.`nDein Gerät.`nDeine Kontrolle."; Body = 'Ein lokaler, verschlüsselter TOTP-Tresor – ganz ohne Cloudkonto.'; Chips = @('LOKAL GESPEICHERT', 'VERSCHLÜSSELT'); Size = 66 }
            @{ File = '02-search-and-copy.png'; Screenshot = $search; Headline = "Finden. Kopieren.`nWeiterarbeiten."; Body = 'Durchsuche deine Konten sofort und kopiere den aktuellen Code mit einem Klick.'; Chips = @('SCHNELLE SUCHE', 'LIVE-CODES'); Size = 70 }
            @{ File = '03-add-and-import.png'; Screenshot = $add; Headline = "Konten schnell`nhinzufügen."; Body = 'Manuell oder per QR-Code – mit flexiblen TOTP-Einstellungen.'; Chips = @('QR-IMPORT', 'MANUELLE EINGABE'); Size = 70 }
            @{ File = '04-windows-hello.png'; Screenshot = $quickUnlock; Secondary = $helloPrompt; Layout = 'HelloPair'; Headline = "Schnell entsperrt.`nSicher geschützt."; Body = 'Windows Hello für den Alltag. Das Masterpasswort bleibt deine Wiederherstellung.'; Chips = @('WINDOWS HELLO', 'QUICK UNLOCK'); Size = 68 }
            @{ File = '05-import-export.png'; Screenshot = $importExport; Secondary = $importQr; Layout = 'ImportExportPair'; Headline = "Importieren. Exportieren.`nSicher wechseln."; Body = 'Verschlüsselte Backups übertragen, Google-Authenticator-QRs importieren oder Einzelkonten per Kamera scannen.'; Chips = @('GOOGLE-QR-IMPORT', 'KAMERA ODER DATEI'); Size = 56 }
        )
        'fr-FR' = @(
            @{ File = '01-local-vault.png'; Screenshot = $dashboard; Headline = "Vos codes.`nVotre appareil.`nVotre contrôle."; Body = 'Un coffre TOTP local et chiffré, sans compte cloud.'; Chips = @('STOCKAGE LOCAL', 'CHIFFRÉ'); Size = 64 }
            @{ File = '02-search-and-copy.png'; Screenshot = $search; Headline = "Trouvez. Copiez.`nContinuez."; Body = 'Recherchez un compte et copiez le code actuel en un clic.'; Chips = @('RECHERCHE RAPIDE', 'CODES EN DIRECT'); Size = 68 }
            @{ File = '03-add-and-import.png'; Screenshot = $add; Headline = "Ajoutez vos comptes`nen quelques secondes."; Body = 'Saisie manuelle ou import par code QR, avec des réglages TOTP flexibles.'; Chips = @('IMPORT QR', 'SAISIE MANUELLE'); Size = 59 }
            @{ File = '04-windows-hello.png'; Screenshot = $quickUnlock; Secondary = $helloPrompt; Layout = 'HelloPair'; Headline = "Déverrouillage rapide.`nProtection renforcée."; Body = 'Windows Hello au quotidien. Le mot de passe principal reste la méthode de récupération.'; Chips = @('WINDOWS HELLO', 'DÉVERROUILLAGE RAPIDE'); Size = 58 }
            @{ File = '05-import-export.png'; Screenshot = $importExport; Secondary = $importQr; Layout = 'ImportExportPair'; Headline = "Importez. Exportez.`nMigrez sereinement."; Body = 'Transférez des sauvegardes chiffrées, importez les QR Google Authenticator ou scannez un compte par caméra.'; Chips = @('IMPORT QR GOOGLE', 'CAMÉRA OU FICHIER'); Size = 60 }
        )
        'es-ES' = @(
            @{ File = '01-local-vault.png'; Screenshot = $dashboard; Headline = "Tus códigos.`nTu dispositivo.`nTu control."; Body = 'Una bóveda TOTP local y cifrada, sin cuenta en la nube.'; Chips = @('ALMACENAMIENTO LOCAL', 'CIFRADO'); Size = 66 }
            @{ File = '02-search-and-copy.png'; Screenshot = $search; Headline = "Busca. Copia.`nContinúa."; Body = 'Encuentra tus cuentas y copia el código actual con un clic.'; Chips = @('BÚSQUEDA RÁPIDA', 'CÓDIGOS EN VIVO'); Size = 70 }
            @{ File = '03-add-and-import.png'; Screenshot = $add; Headline = "Añade cuentas`nen segundos."; Body = 'Entrada manual o importación mediante QR, con ajustes TOTP flexibles.'; Chips = @('IMPORTAR QR', 'ENTRADA MANUAL'); Size = 68 }
            @{ File = '04-windows-hello.png'; Screenshot = $quickUnlock; Secondary = $helloPrompt; Layout = 'HelloPair'; Headline = "Acceso rápido.`nProtección segura."; Body = 'Windows Hello para el uso diario. La contraseña maestra sigue siendo la recuperación.'; Chips = @('WINDOWS HELLO', 'DESBLOQUEO RÁPIDO'); Size = 66 }
            @{ File = '05-import-export.png'; Screenshot = $importExport; Secondary = $importQr; Layout = 'ImportExportPair'; Headline = "Importa. Exporta.`nCambia con confianza."; Body = 'Transfiere copias cifradas, importa QR de Google Authenticator o escanea una cuenta con la cámara.'; Chips = @('IMPORTAR QR DE GOOGLE', 'CÁMARA O ARCHIVO'); Size = 60 }
        )
    }

    foreach ($culture in $Cultures) {
        $resolvedOutput = Join-Path $resolvedOutputRoot "$culture/marketing"
        [IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

        foreach ($slide in $localizedCampaigns[$culture]) {
            $arguments = @{
                Background = $background
                Icon = $icon
                Screenshot = $slide.Screenshot
                Headline = $slide.Headline
                Body = $slide.Body
                Chips = $slide.Chips
                HeadlineSize = $slide.Size
                Destination = Join-Path $resolvedOutput $slide.File
            }
            if ($slide.ContainsKey('Secondary')) {
                $arguments.SecondaryScreenshot = $slide.Secondary
            }
            if ($slide.ContainsKey('Layout')) {
                $arguments.Layout = $slide.Layout
            }
            New-MarketingScreenshot @arguments
        }

        Write-Output "Store marketing screenshots created for $culture in $resolvedOutput"
    }
}
finally {
    $importQr.Dispose(); $importExport.Dispose(); $quickUnlock.Dispose(); $helloPrompt.Dispose()
    $add.Dispose(); $search.Dispose(); $dashboard.Dispose()
    $icon.Dispose(); $background.Dispose()
}
