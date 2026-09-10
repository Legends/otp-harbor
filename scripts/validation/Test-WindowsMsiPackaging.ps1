<#
.SYNOPSIS
Validates the Windows MSI packaging implementation and workflow.

.DESCRIPTION
Statically checks WiX source generation, installer UI/resources, per-machine installation, shortcuts, optional launch behavior, update policy, manifest classification, MSI validation, and release-workflow integration.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$packagerPath = Join-Path $repositoryRoot 'scripts/release/New-WindowsMsi.ps1'
$setupPackagerPath = Join-Path $repositoryRoot 'scripts/release/New-WindowsSetup.ps1'
$workflowPath = Join-Path $repositoryRoot '.github/workflows/build-and-test.yml'
$manifestPath = Join-Path $repositoryRoot 'scripts/release/New-ReleaseArtifactManifest.ps1'
$installerResourceRoot = Join-Path $repositoryRoot 'scripts/release/installer'
$customInstallerProject = Join-Path $repositoryRoot 'TOTP.Installer/TOTP.Installer.csproj'
$customInstallerView = Join-Path $repositoryRoot 'TOTP.Installer/Views/InstallerWindow.axaml'

$packager = Get-Content -LiteralPath $packagerPath -Raw
$setupPackager = Get-Content -LiteralPath $setupPackagerPath -Raw
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
    'DllEntry="WixUnelevatedShellExec"',
    'WixUnelevatedShellExecTarget',
    'New-WindowsSetup.ps1',
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
    'exactly five public Windows/Linux artifacts',
    'wix extension add --global WixToolset.Util.wixext/5.0.2',
    'wix extension add --global WixToolset.BootstrapperApplications.wixext/5.0.2',
    'OTP-Harbor-windows-x64-setup-${{ steps.versioning.outputs.release_version }}.exe'
)) {
    if (-not $workflow.Contains($control, [StringComparison]::Ordinal)) {
        throw "The release workflow is missing MSI control: $control"
    }
}

$expectedLocalizationKeys = @(
    'InstallerManufacturer',
    'InstallerShortcutDescription',
    'InstallerLaunchApplication',
    'InstallerSuccessMessage',
    'InstallerNewerVersionInstalled'
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

[xml]$bundle = Get-Content (Join-Path $installerResourceRoot 'HarborSetup.wxs') -Raw
if ($bundle.Wix.Bundle.Chain.MsiPackage.MsiProperty.Name -ne 'INSTALLFOLDER' -or
    $bundle.Wix.Bundle.Chain.MsiPackage.GetAttribute('DisplayInternalUICondition', 'http://wixtoolset.org/schemas/v4/wxs/bal') -ne '0') {
    throw 'The bundle must pass its selected folder to the MSI and suppress the internal MSI UI.'
}
if ($bundle.Wix.Bundle.BootstrapperApplication.SourceFile -notmatch 'TOTP\.Installer\.exe' -or
    $bundle.OuterXml -match 'WixStandardBootstrapperApplication') {
    throw 'The setup must use the custom Avalonia bootstrapper instead of the classic WiX standard UI.'
}

$customInstallerProjectText = Get-Content -LiteralPath $customInstallerProject -Raw
$customInstallerViewText = Get-Content -LiteralPath $customInstallerView -Raw
foreach ($control in @(
    '<OutputType>WinExe</OutputType>',
    '<EnableDynamicLoading>true</EnableDynamicLoading>',
    'WixToolset.BootstrapperApplicationApi',
    'Avalonia.Desktop'
)) {
    if (-not $customInstallerProjectText.Contains($control, [StringComparison]::Ordinal)) {
        throw "The custom installer project is missing required control: $control"
    }
}

foreach ($control in @(
    '& $DotNetExecutable publish',
    '--self-contained true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    'TOTP.Installer/TOTP.Installer.csproj',
    'InstallerPayloads.wxi',
    'Remove-Item -LiteralPath $installerUiDirectory -Recurse -Force'
)) {
    if (-not $setupPackager.Contains($control, [StringComparison]::Ordinal)) {
        throw "The custom setup packager is missing required control: $control"
    }
}
foreach ($control in @(
    'WindowDecorations="None"',
    'SystemDecorations="None"',
    'IsVisible="{Binding IsWelcome}"',
    'IsVisible="{Binding IsLicense}"',
    'IsVisible="{Binding IsMaintenance}"',
    'IsVisible="{Binding IsApplying}"',
    'IsVisible="{Binding IsSuccess}"',
    'IsVisible="{Binding IsFailure}"'
)) {
    $present = $customInstallerViewText.Contains($control, [StringComparison]::Ordinal)
    if ($control -eq 'SystemDecorations="None"') {
        if ($present) { throw 'The custom installer must not regress to the obsolete classic-window property.' }
    }
    elseif (-not $present) {
        throw "The custom installer view is missing required control: $control"
    }
}

$expectedSetupKeys = $null
$viewTextKeys = @([regex]::Matches($customInstallerViewText, 'Text\.([A-Za-z0-9]+)') |
    ForEach-Object { $_.Groups[1].Value } |
    Where-Object { $_ -ne 'LicenseBody' } |
    Sort-Object -Unique)
foreach ($culture in @('en-us', 'de-de', 'fr-fr', 'es-es')) {
    [xml]$setupLocale = Get-Content (Join-Path $installerResourceRoot "Setup.$culture.wxl") -Raw
    $setupKeys = @($setupLocale.WixLocalization.String.Id | Sort-Object)
    if ($null -eq $expectedSetupKeys) { $expectedSetupKeys = $setupKeys }
    if (Compare-Object $expectedSetupKeys $setupKeys) { throw "Incomplete setup locale: $culture" }
    foreach ($key in $viewTextKeys) {
        if ($key -notin $setupKeys) { throw "Missing custom setup string $key in $culture" }
    }
    if ($setupLocale.WixLocalization.String | Where-Object { [string]::IsNullOrWhiteSpace($_.Value) }) {
        throw "Empty setup translation in $culture"
    }
    $licenseTextPath = Join-Path (Split-Path $customInstallerProject) "Localization/License.$culture.txt"
    if (-not (Test-Path -LiteralPath $licenseTextPath -PathType Leaf) -or
        [string]::IsNullOrWhiteSpace((Get-Content -LiteralPath $licenseTextPath -Raw))) {
        throw "Missing custom setup license text for $culture"
    }
}
Write-Host 'Windows MSI and custom Avalonia setup packaging controls are present.'
