[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$packagerPath = Join-Path $repositoryRoot 'scripts/release/New-WindowsMsi.ps1'
$workflowPath = Join-Path $repositoryRoot '.github/workflows/build-and-test.yml'
$manifestPath = Join-Path $repositoryRoot 'scripts/release/New-ReleaseArtifactManifest.ps1'
$installerResourceRoot = Join-Path $repositoryRoot 'scripts/release/installer'

$packager = Get-Content -LiteralPath $packagerPath -Raw
$workflow = Get-Content -LiteralPath $workflowPath -Raw
$manifest = Get-Content -LiteralPath $manifestPath -Raw

foreach ($control in @(
    'Scope="perMachine"',
    'AllowSameVersionUpgrades="yes"',
    '-DistributionMode package-manager',
    '-DisableUpdates',
    "Remove-Item -LiteralPath `$bundledUpdater",
    'ProgramFiles6432Folder',
    'Advertise="yes"',
    'Directory="DesktopFolder"',
    'Icon="ApplicationIcon.ico"',
    'ARPPRODUCTICON',
    'WixUI_InstallDir',
    'WixUIDialogBmp',
    'WixUIBannerBmp',
    'WIXUI_EXITDIALOGOPTIONALCHECKBOXTEXT',
    'WIXUI_EXITDIALOGOPTIONALTEXT',
    'CustomAction Id="LaunchApplication"',
    'Condition="WIXUI_EXITDIALOGOPTIONALCHECKBOX = 1 AND NOT Installed"',
    'otp-harbor.installed',
    '-ext WixToolset.UI.wixext/5.0.2',
    'msi validate -sice ICE61',
    'ProgramMenuFolder'
)) {
    if (-not $packager.Contains($control, [StringComparison]::Ordinal)) {
        throw "The Windows MSI packager is missing required control: $control"
    }
}

foreach ($control in @(
    'dotnet tool install --global wix --version 5.0.2',
    'wix extension add --global WixToolset.UI.wixext/5.0.2',
    '-p:PublishReadyToRun=true',
    'New-WindowsMsi.ps1',
    'OTP-Harbor-windows-x64-${{ steps.versioning.outputs.release_version }}.msi',
    'exactly five Windows/Linux artifacts'
)) {
    if (-not $workflow.Contains($control, [StringComparison]::Ordinal)) {
        throw "The release workflow is missing MSI control: $control"
    }
}

$expectedLocalizationKeys = @(
    'InstallerManufacturer',
    'InstallerShortcutDescription',
    'InstallerLaunchApplication',
    'InstallerSuccessMessage'
)
$localizationFiles = @(Get-ChildItem -LiteralPath $installerResourceRoot -Filter 'OTP-Harbor.*.wxl' -File)
if ($localizationFiles.Count -ne 4) {
    throw 'The Windows installer must provide all four supported localization resource files.'
}

$licenseFiles = @(Get-ChildItem -LiteralPath $installerResourceRoot -Filter 'License.*.rtf' -File)
if ($licenseFiles.Count -ne 4) {
    throw 'The Windows installer must provide all four supported localized license and preview-warning files.'
}
foreach ($localizationFile in $localizationFiles) {
    [xml]$localization = Get-Content -LiteralPath $localizationFile.FullName -Raw
    $keys = @($localization.DocumentElement.String | ForEach-Object { $_.Id })
    foreach ($key in $expectedLocalizationKeys) {
        if ($key -notin $keys) {
            throw "$($localizationFile.Name) is missing installer localization key: $key"
        }
    }
}

foreach ($bitmap in @(
    @{ Name = 'InstallerDialog.bmp'; Width = 493; Height = 312 },
    @{ Name = 'InstallerBanner.bmp'; Width = 493; Height = 58 }
)) {
    $bitmapPath = Join-Path $installerResourceRoot $bitmap.Name
    $bytes = [IO.File]::ReadAllBytes($bitmapPath)
    if ($bytes.Length -lt 26 -or $bytes[0] -ne 0x42 -or $bytes[1] -ne 0x4D) {
        throw "$($bitmap.Name) is not a valid Windows bitmap."
    }
    $width = [BitConverter]::ToInt32($bytes, 18)
    $height = [Math]::Abs([BitConverter]::ToInt32($bytes, 22))
    if ($width -ne $bitmap.Width -or $height -ne $bitmap.Height) {
        throw "$($bitmap.Name) must be $($bitmap.Width)x$($bitmap.Height) pixels."
    }
}

if (-not $manifest.Contains('format = "msi"', [StringComparison]::Ordinal) -or
    -not $manifest.Contains('ownership = "windows-installer"', [StringComparison]::Ordinal)) {
    throw 'The release manifest does not model the MSI as a separate installer-owned artifact.'
}

Write-Host 'Windows MSI packaging controls are present.'
