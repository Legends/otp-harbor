<#
.SYNOPSIS
Builds an unsigned Microsoft Store submission MSIX for OTP Harbor.

.DESCRIPTION
Publishes the Windows application, renders the reviewed Appx manifest and visual assets with Partner Center identity data, disables direct-app updating for Store distribution, validates package boundaries, and creates the MSIX below artifacts.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9.-]{1,48}[A-Za-z0-9]$')]
    [string]$IdentityName,

    [Parameter(Mandatory)]
    [ValidatePattern('^CN=.+')]
    [string]$Publisher,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$PublisherDisplayName,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+\.0$')]
    [string]$Version,

    [string]$OutputDirectory = 'artifacts/store',

    [string]$PublishDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$resolvedOutput = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
$allowedOutputRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
if (-not $resolvedOutput.StartsWith($allowedOutputRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The Store output directory must remain below the repository artifacts directory.'
}

$packageVersion = [Version]$Version
$invalidVersionComponent = @(
    $packageVersion.Major,
    $packageVersion.Minor,
    $packageVersion.Build,
    $packageVersion.Revision
) | Where-Object { $_ -lt 0 -or $_ -gt 65535 } | Select-Object -First 1
if ($packageVersion.Major -eq 0 -or $null -ne $invalidVersionComponent) {
    throw 'The Store version must contain four values from 0 through 65535, with a non-zero major version.'
}
if ($packageVersion.Revision -ne 0) {
    throw 'The fourth Store package-version component is reserved by Microsoft and must be 0.'
}

New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
$workRoot = Join-Path $resolvedOutput ('stage-' + [Guid]::NewGuid().ToString('N'))
$packageRoot = Join-Path $workRoot 'package'
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

function ConvertTo-XmlAttributeValue {
    param([Parameter(Mandatory)][string]$Value)
    return [Security.SecurityElement]::Escape($Value)
}

function Find-MakeAppx {
    $command = Get-Command makeappx.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $candidates = @(Get-ChildItem -LiteralPath $kitsRoot -Filter makeappx.exe -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '[\\/]x64[\\/]makeappx\.exe$' } |
        Sort-Object { [Version]$_.Directory.Parent.Name } -Descending)
    if ($candidates.Count -eq 0) {
        throw 'MakeAppx.exe was not found. Install the Windows 10 or Windows 11 SDK.'
    }
    return $candidates[0].FullName
}

function New-StoreImage {
    param(
        [Parameter(Mandatory)][System.Drawing.Image]$Source,
        [Parameter(Mandatory)][int]$Width,
        [Parameter(Mandatory)][int]$Height,
        [Parameter(Mandatory)][string]$Path
    )

    $bitmap = [Drawing.Bitmap]::new($Width, $Height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.Clear([Drawing.ColorTranslator]::FromHtml('#0C1C33'))
            $targetSize = [Math]::Floor([Math]::Min($Width, $Height) * 0.76)
            $left = [Math]::Floor(($Width - $targetSize) / 2)
            $top = [Math]::Floor(($Height - $targetSize) / 2)
            $graphics.DrawImage($Source, [Drawing.Rectangle]::new($left, $top, $targetSize, $targetSize))
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

try {
    if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
        $productVersion = '{0}.{1}.{2}' -f $packageVersion.Major, $packageVersion.Minor, $packageVersion.Build
        & dotnet publish `
            (Join-Path $repositoryRoot 'TOTP.UI.Avalonia.Desktop/TOTP.UI.Avalonia.Desktop.csproj') `
            --configuration Release `
            --runtime win-x64 `
            --self-contained true `
            --nologo `
            --verbosity minimal `
            --output $packageRoot `
            -p:Version=$productVersion `
            -p:FileVersion=$Version `
            -p:InformationalVersion=$productVersion
        if ($LASTEXITCODE -ne 0) { throw 'The Windows publish step failed.' }
    }
    else {
        $resolvedPublish = (Resolve-Path -LiteralPath $PublishDirectory).Path
        foreach ($item in Get-ChildItem -LiteralPath $resolvedPublish -Force) {
            Copy-Item -LiteralPath $item.FullName -Destination $packageRoot -Recurse -Force
        }
    }

    & (Join-Path $repositoryRoot 'scripts/release/Prepare-AvaloniaReleasePayload.ps1') `
        -PublishDirectory $packageRoot
    & (Join-Path $repositoryRoot 'scripts/release/Set-PackageUpdatePolicy.ps1') `
        -PackageDirectory $packageRoot `
        -DistributionMode store `
        -Channel stable `
        -DisableUpdates

    $updaterDirectory = Join-Path $packageRoot 'TOTP.Updater'
    if (Test-Path -LiteralPath $updaterDirectory) {
        $resolvedUpdater = (Resolve-Path -LiteralPath $updaterDirectory).Path
        if (-not $resolvedUpdater.StartsWith($packageRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to remove an updater directory outside the Store package staging root.'
        }
        Remove-Item -LiteralPath $resolvedUpdater -Recurse -Force
    }

    $assetsDirectory = Join-Path $packageRoot 'Assets'
    New-Item -ItemType Directory -Path $assetsDirectory -Force | Out-Null
    Add-Type -AssemblyName System.Drawing
    $sourceIcon = [Drawing.Image]::FromFile(
        (Join-Path $repositoryRoot 'TOTP.UI.Avalonia.Desktop/Assets/Icons/app-1024.png'))
    try {
        New-StoreImage $sourceIcon 44 44 (Join-Path $assetsDirectory 'Square44x44Logo.png')
        New-StoreImage $sourceIcon 150 150 (Join-Path $assetsDirectory 'Square150x150Logo.png')
        New-StoreImage $sourceIcon 50 50 (Join-Path $assetsDirectory 'StoreLogo.png')
        New-StoreImage $sourceIcon 310 150 (Join-Path $assetsDirectory 'Wide310x150Logo.png')
        New-StoreImage $sourceIcon 620 300 (Join-Path $assetsDirectory 'SplashScreen.png')
    }
    finally {
        $sourceIcon.Dispose()
    }

    $manifestTemplate = [IO.File]::ReadAllText(
        (Join-Path $repositoryRoot 'packaging/windows-store/AppxManifest.xml.template'))
    $manifest = $manifestTemplate.Replace(
        '__IDENTITY_NAME__',
        (ConvertTo-XmlAttributeValue $IdentityName))
    $manifest = $manifest.Replace(
        '__PUBLISHER__',
        (ConvertTo-XmlAttributeValue $Publisher))
    $manifest = $manifest.Replace(
        '__PUBLISHER_DISPLAY_NAME__',
        (ConvertTo-XmlAttributeValue $PublisherDisplayName))
    $manifest = $manifest.Replace('__VERSION__', $Version)
    [IO.File]::WriteAllText(
        (Join-Path $packageRoot 'AppxManifest.xml'),
        $manifest,
        [Text.UTF8Encoding]::new($false))

    $safeVersion = $Version.Replace('.', '-')
    $msixPath = Join-Path $resolvedOutput "OTP-Harbor_${safeVersion}_x64.msix"
    if (Test-Path -LiteralPath $msixPath) { Remove-Item -LiteralPath $msixPath -Force }
    $makeAppx = Find-MakeAppx
    & $makeAppx pack /d $packageRoot /p $msixPath /o
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $msixPath -PathType Leaf)) {
        throw 'MakeAppx failed to produce the Store MSIX package.'
    }

    $hash = (Get-FileHash -LiteralPath $msixPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $metadata = [ordered]@{
        schemaVersion = 1
        package = [IO.Path]::GetFileName($msixPath)
        identityName = $IdentityName
        publisher = $Publisher
        version = $Version
        architecture = 'x64'
        distribution = 'microsoft-store-only'
        signed = $false
        sha256 = $hash
    }
    $metadataPath = Join-Path $resolvedOutput 'store-package.json'
    [IO.File]::WriteAllText(
        $metadataPath,
        (($metadata | ConvertTo-Json -Depth 4) + "`n"),
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        (Join-Path $resolvedOutput 'SHA256SUMS'),
        "$hash  $([IO.Path]::GetFileName($msixPath))`n",
        [Text.UTF8Encoding]::new($false))

    Write-Output "Created unsigned Microsoft Store submission package: $msixPath"
    Write-Output 'Do not distribute this unsigned MSIX directly. Upload it only through Partner Center.'
}
finally {
    $resolvedWorkRoot = [IO.Path]::GetFullPath($workRoot)
    if (-not $resolvedWorkRoot.StartsWith($resolvedOutput, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Split-Path -Leaf $resolvedWorkRoot).StartsWith('stage-', [StringComparison]::Ordinal)) {
        throw 'Refusing to remove an unexpected Store staging directory.'
    }
    if (Test-Path -LiteralPath $resolvedWorkRoot) {
        Remove-Item -LiteralPath $resolvedWorkRoot -Recurse -Force
    }
}
