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
if (-not (Test-Path -LiteralPath $mainExecutable -PathType Leaf)) {
    throw "The Windows publish payload is missing its application executable: $mainExecutable"
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

    $bundledUpdater = Join-Path $payloadRoot 'TOTP.Updater'
    if (Test-Path -LiteralPath $bundledUpdater) {
        Remove-Item -LiteralPath $bundledUpdater -Recurse -Force
    }

    $wixSource = @"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Package Name="OTP Harbor"
           Manufacturer="OTP Harbor contributors"
           Version="$ProductVersion"
           Language="1033"
           Scope="perMachine"
           UpgradeCode="9B04662E-C2D9-4D6C-9657-62091A4342C4">
    <MajorUpgrade AllowSameVersionUpgrades="yes"
                  DowngradeErrorMessage="A newer version of OTP Harbor is already installed." />
    <MediaTemplate EmbedCab="yes" />

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
                      Description="Local-first TOTP authenticator"
                      Directory="ApplicationProgramsFolder"
                      Advertise="yes"
                      WorkingDirectory="INSTALLFOLDER" />
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
  </Package>
</Wix>
"@
    [IO.File]::WriteAllText($wixSourcePath, $wixSource, [Text.UTF8Encoding]::new($false))

    & $WixExecutable build `
        -arch x64 `
        -d "PayloadDirectory=$payloadRoot" `
        -o $outputPath `
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
