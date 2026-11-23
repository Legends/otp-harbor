#requires -Version 7.0
<#
.SYNOPSIS
Installs the pinned Android NDK needed for NativeAOT compilation.
#>
[CmdletBinding()]
param([string]$SdkDirectory)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($SdkDirectory)) {
    $SdkDirectory = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($SdkDirectory)) { throw 'Supply -SdkDirectory or configure ANDROID_HOME.' }
$ndkSdkRoot = (Resolve-Path -LiteralPath $SdkDirectory).Path
$ndkVersion = '28.2.13676358'
$ndkDirectory = Join-Path $ndkSdkRoot "ndk/$ndkVersion"
if (-not (Test-Path -LiteralPath (Join-Path $ndkDirectory 'source.properties') -PathType Leaf)) {
    $ndkManagerName = if ($IsWindows) { 'sdkmanager.bat' } else { 'sdkmanager' }
    $ndkManager = Join-Path $ndkSdkRoot "cmdline-tools/latest/bin/$ndkManagerName"
    if (-not (Test-Path -LiteralPath $ndkManager -PathType Leaf)) { throw 'Android SDK command-line tools are missing.' }
    & $ndkManager "--sdk_root=$ndkSdkRoot" "ndk;$ndkVersion"
    if ($LASTEXITCODE -ne 0) { throw 'The pinned Android NDK could not be installed.' }
}
if ((Get-Content (Join-Path $ndkDirectory 'source.properties') -Raw) -notmatch '(?m)^Pkg\.Revision\s*=\s*28\.2\.13676358\s*$') {
    throw 'The installed NDK version does not match the validated toolchain.'
}
$env:ANDROID_NDK_HOME = $ndkDirectory
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_ENV)) { "ANDROID_NDK_HOME=$ndkDirectory" >> $env:GITHUB_ENV }
Write-Output "Android NativeAOT NDK $ndkVersion is ready: $ndkDirectory"
