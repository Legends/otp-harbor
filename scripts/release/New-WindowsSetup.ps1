<#
.SYNOPSIS
Builds the frameless OTP Harbor setup around the validated Windows MSI.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$MsiPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(?:-rc\d+)?$')][string]$ReleaseVersion,
    [ValidateSet('en-us', 'de-de', 'fr-fr', 'es-es')][string]$Culture = 'en-us',
    [string]$WixExecutable = 'wix'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$resolvedMsi = (Resolve-Path -LiteralPath $MsiPath).Path
$resourceRoot = Join-Path $PSScriptRoot 'installer'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
$outputPath = Join-Path $resolvedOutput "OTP-Harbor-windows-x64-setup-$ReleaseVersion.exe"

& $WixExecutable build -arch x64 `
    -ext WixToolset.BootstrapperApplications.wixext/5.0.2 `
    -ext WixToolset.Util.wixext/5.0.2 `
    -d "ReleaseVersion=$ReleaseVersion" `
    -d "MsiPath=$resolvedMsi" `
    -d "Culture=$Culture" `
    -d "ResourceDirectory=$resourceRoot" `
    -d "ApplicationIcon=$(Join-Path $repoRoot 'TOTP.UI.Avalonia.Desktop/Assets/Icons/app.ico')" `
    -d "ApplicationLogo=$(Join-Path $repoRoot 'TOTP.UI.Avalonia.Desktop/Assets/Icons/app-128.png')" `
    -o $outputPath `
    (Join-Path $resourceRoot 'HarborSetup.wxs')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $outputPath -PathType Leaf)) {
    throw 'The OTP Harbor setup bundle could not be built.'
}
Write-Output $outputPath
