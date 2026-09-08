[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-rc\d+)?$')]
    [string]$ReleaseVersion,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d{1,3}\.\d{1,3}\.\d{1,5}$')]
    [string]$ProductVersion,

    [string]$WixExecutable = 'wix'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsWindows) {
    throw 'Windows MSI packages can only be built on Windows.'
}
if (-not (Get-Command $WixExecutable -ErrorAction SilentlyContinue)) {
    throw "WiX is unavailable: $WixExecutable"
}

$resolvedPublish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
$mainExecutable = Join-Path $resolvedPublish 'TOTP.UI.Avalonia.Desktop.exe'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$installerResourceRoot = Join-Path $PSScriptRoot 'installer'
$localizationPath = Join-Path $installerResourceRoot 'OTP-Harbor.en-us.wxl'
$licensePath = Join-Path $installerResourceRoot 'License.en-us.rtf'
$dialogBitmapPath = Join-Path $installerResourceRoot 'InstallerDialog.bmp'
$bannerBitmapPath = Join-Path $installerResourceRoot 'InstallerBanner.bmp'
$applicationIconPath = Join-Path $repositoryRoot 'TOTP.UI.Avalonia.Desktop/Assets/Icons/app.ico'
if (-not (Test-Path -LiteralPath $mainExecutable -PathType Leaf)) {
    throw "The Windows publish payload is missing its application executable: $mainExecutable"
}
foreach ($resourcePath in @(
    $localizationPath,
    $licensePath,
    $dialogBitmapPath,
    $bannerBitmapPath,
    $applicationIconPath
)) {
    if (-not (Test-Path -LiteralPath $resourcePath -PathType Leaf)) {
        throw "The Windows MSI installer resource is missing: $resourcePath"
    }
}

$payloadFiles = @(Get-ChildItem -LiteralPath $resolvedPublish -Recurse -File)
if ($payloadFiles.Count -eq 0) {
    throw 'The Windows publish payload is empty.'
}
if ($payloadFiles | Where-Object {
    ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
}) {
    throw 'The Windows MSI payload must not contain reparse points.'
}

$versionParts = @($ProductVersion.Split('.') | ForEach-Object { [int]$_ })
if ($versionParts[0] -gt 255 -or $versionParts[1] -gt 255 -or $versionParts[2] -gt 65535) {
    throw 'ProductVersion exceeds Windows Installer numeric limits.'
}

$workRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'otp-harbor-msi-' + [Guid]::NewGuid().ToString('N'))
$payloadRoot = Join-Path $workRoot 'payload'
$wixSourcePath = Join-Path $workRoot 'OTP-Harbor.wxs'
$outputName = "OTP-Harbor-windows-x64-$ReleaseVersion.msi"
$outputPath = Join-Path $resolvedOutput $outputName

try {
    New-Item -ItemType Directory -Path $payloadRoot, $resolvedOutput -Force | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $resolvedPublish) {
        Copy-Item -LiteralPath $item.FullName -Destination $payloadRoot -Recurse
    }

    $channel = if ($ReleaseVersion -match '-rc\d+$') { 'rc' } else { 'stable' }
    & (Join-Path $PSScriptRoot 'Set-PackageUpdatePolicy.ps1') `
        -PackageDirectory $payloadRoot `
        -DistributionMode package-manager `
        -Channel $channel `
        -DisableUpdates

    [IO.File]::WriteAllText(
        (Join-Path $payloadRoot 'otp-harbor.installed'),
        "windows-installer`n",
        [Text.UTF8Encoding]::new($false))

    $bundledUpdater = Join-Path $payloadRoot 'TOTP.Updater'
    if (Test-Path -LiteralPath $bundledUpdater) {
        Remove-Item -LiteralPath $bundledUpdater -Recurse -Force
    }

    $wixSource = @"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"
     xmlns:ui="http://wixtoolset.org/schemas/v4/wxs/ui">
  <Package Name="OTP Harbor"
           Manufacturer="!(loc.InstallerManufacturer)"
           Version="$ProductVersion"
           Language="1033"
           Scope="perMachine"
           UpgradeCode="9B04662E-C2D9-4D6C-9657-62091A4342C4">
    <MajorUpgrade AllowSameVersionUpgrades="yes"
                  DowngradeErrorMessage="A newer version of OTP Harbor is already installed." />
    <MediaTemplate EmbedCab="yes" />
    <Icon Id="ApplicationIcon.ico" SourceFile="`$(var.ApplicationIcon)" />
    <Property Id="ARPPRODUCTICON" Value="ApplicationIcon.ico" />
    <SetProperty Id="WIXUI_EXITDIALOGOPTIONALCHECKBOX"
                 Value="1"
                 Before="CostFinalize"
                 Sequence="ui"
                 Condition="NOT Installed" />
    <SetProperty Id="WIXUI_EXITDIALOGOPTIONALCHECKBOXTEXT"
                 Value="!(loc.InstallerLaunchApplication)"
                 Before="CostFinalize"
                 Sequence="ui"
                 Condition="NOT Installed" />
    <SetProperty Id="WIXUI_EXITDIALOGOPTIONALTEXT"
                 Value="!(loc.InstallerSuccessMessage)"
                 Before="CostFinalize"
                 Sequence="ui"
                 Condition="NOT Installed" />

    <ui:WixUI Id="WixUI_InstallDir" InstallDirectory="INSTALLFOLDER" />
    <WixVariable Id="WixUILicenseRtf" Value="`$(var.InstallerLicense)" />
    <WixVariable Id="WixUIDialogBmp" Value="`$(var.InstallerDialogBitmap)" />
    <WixVariable Id="WixUIBannerBmp" Value="`$(var.InstallerBannerBitmap)" />

    <StandardDirectory Id="ProgramFiles6432Folder">
      <Directory Id="INSTALLFOLDER" Name="OTP Harbor">
        <Files Include="`$(var.PayloadDirectory)\**">
          <Exclude Files="`$(var.PayloadDirectory)\TOTP.UI.Avalonia.Desktop.exe" />
        </Files>
        <Component Id="ApplicationExecutableComponent"
                   Guid="8C7731F7-469C-45AF-B576-7D3467BD082D">
          <File Id="ApplicationExecutable"
                Source="`$(var.PayloadDirectory)\TOTP.UI.Avalonia.Desktop.exe"
                KeyPath="yes">
            <Shortcut Id="ApplicationStartMenuShortcut"
                      Name="OTP Harbor"
                      Description="!(loc.InstallerShortcutDescription)"
                      Directory="ApplicationProgramsFolder"
                      Advertise="yes"
                      WorkingDirectory="INSTALLFOLDER"
                      Icon="ApplicationIcon.ico" />
            <Shortcut Id="ApplicationDesktopShortcut"
                      Name="OTP Harbor"
                      Description="!(loc.InstallerShortcutDescription)"
                      Directory="DesktopFolder"
                      Advertise="yes"
                      WorkingDirectory="INSTALLFOLDER"
                      Icon="ApplicationIcon.ico" />
          </File>
          <RemoveFolder Id="RemoveApplicationProgramsFolder"
                        Directory="ApplicationProgramsFolder"
                        On="uninstall" />
        </Component>
      </Directory>
    </StandardDirectory>

    <StandardDirectory Id="ProgramMenuFolder">
      <Directory Id="ApplicationProgramsFolder" Name="OTP Harbor" />
    </StandardDirectory>

    <StandardDirectory Id="DesktopFolder" />

    <CustomAction Id="LaunchApplication"
                  FileRef="ApplicationExecutable"
                  ExeCommand=""
                  Execute="immediate"
                  Impersonate="yes"
                  Return="asyncNoWait" />
    <UI>
      <Publish Dialog="ExitDialog"
               Control="Finish"
               Event="DoAction"
               Value="LaunchApplication"
               Condition="WIXUI_EXITDIALOGOPTIONALCHECKBOX = 1 AND NOT Installed" />
    </UI>
  </Package>
</Wix>
"@
    [IO.File]::WriteAllText($wixSourcePath, $wixSource, [Text.UTF8Encoding]::new($false))

    & $WixExecutable build `
        -arch x64 `
        -culture en-us `
        -ext WixToolset.UI.wixext/5.0.2 `
        -d "PayloadDirectory=$payloadRoot" `
        -d "ApplicationIcon=$applicationIconPath" `
        -d "InstallerLicense=$licensePath" `
        -d "InstallerDialogBitmap=$dialogBitmapPath" `
        -d "InstallerBannerBitmap=$bannerBitmapPath" `
        -o $outputPath `
        $localizationPath `
        $wixSourcePath
    if ($LASTEXITCODE -ne 0) {
        throw "WiX failed to create $outputName."
    }
    if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf) -or
        (Get-Item -LiteralPath $outputPath).Length -le 0) {
        throw "WiX did not create a non-empty MSI package: $outputPath"
    }

    # Release candidates share their three-part MSI ProductVersion. Same-version
    # major upgrades are deliberate, so only ICE61 is suppressed.
    & $WixExecutable msi validate -sice ICE61 $outputPath
    if ($LASTEXITCODE -ne 0) {
        throw "Windows Installer validation failed for $outputName."
    }

    Write-Output $outputPath
}
finally {
    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force
    }
}
