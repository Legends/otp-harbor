#requires -Version 5.1
<#
.SYNOPSIS
Creates production-parser-validated synthetic authenticator imports and copies them to Android Downloads.
.EXAMPLE
.\Copy-AuthenticatorImportTestKitToAndroid.ps1 -Device emulator-5554 -AdbPath D:\android-platform-tools\adb.exe
.EXAMPLE
.\Copy-AuthenticatorImportTestKitToAndroid.ps1 -Device emulator-5554 -KitDirectory E:\path\to\a\generated\kit
#>
[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9_.:-]+$')][string]$Device = 'emulator-5554',
    [string]$AdbPath = 'adb',
    [string]$KitDirectory,
    [ValidatePattern('^/sdcard/Download/[A-Za-z0-9._-]+$')]
    [string]$RemoteRoot = '/sdcard/Download/OTP-Harbor-Import-Tests'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
. (Join-Path $PSScriptRoot '../shared/Testing.Common.ps1')
$repositoryRoot = Get-OtpHarborRepositoryRoot -StartDirectory $PSScriptRoot

$adb = Get-Command $AdbPath -ErrorAction SilentlyContinue
if ($null -eq $adb) {
    $sdkRoots = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT)
    if ($env:LOCALAPPDATA) { $sdkRoots += [IO.Path]::Combine($env:LOCALAPPDATA, 'Android', 'Sdk') }
    foreach ($sdkRoot in $sdkRoots) {
        if (-not $sdkRoot) { continue }
        $candidate = [IO.Path]::Combine($sdkRoot, 'platform-tools', 'adb.exe')
        if ([IO.File]::Exists($candidate)) { $adb = Get-Command $candidate; break }
    }
}
if ($null -eq $adb) { throw 'ADB was not found. Supply -AdbPath with the full adb.exe path.' }
$adbExecutable = $adb.Source
$state = (& $adbExecutable -s $Device get-state | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $state -ne 'device') { throw "Android device '$Device' is not connected and authorized." }

if (-not $KitDirectory) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'The .NET 10 SDK is required to generate the test kit.' }
    $KitDirectory = Join-Path $repositoryRoot ('artifacts/shared/testing/authenticator-import-kit-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
    $project = Join-Path $repositoryRoot 'scripts/testing/fixtures/authenticator-imports/AuthenticatorImportFixtures.csproj'
    & dotnet run --project $project --configuration Debug -- $KitDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Fixture generation/production-parser validation failed. Nothing was copied to Android.' }
}
$KitDirectory = [IO.Path]::GetFullPath($KitDirectory)
$manifestPath = Join-Path $KitDirectory 'manifest.json'
if (-not [IO.File]::Exists($manifestPath)) { throw 'This is not a complete validated import kit: manifest.json is missing.' }
$manifest = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($manifestPath))
if ($manifest.synthetic -ne $true -or $manifest.accountWrites -ne 'in-memory only' -or @($manifest.files).Count -eq 0) {
    throw 'The manifest does not describe a validated synthetic import kit.'
}
function Get-FixtureHash([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hash.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $hash.Dispose(); $stream.Dispose() }
}

$files = @($manifest.files) + @([pscustomobject]@{ path = 'manifest.json'; sha256 = Get-FixtureHash $manifestPath })
$localPrefix = $KitDirectory.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
foreach ($file in $files) {
    if ($file.path -notmatch '^[A-Za-z0-9._/-]+$' -or $file.path -match '(^|/)\.\.(/|$)' -or [IO.Path]::IsPathRooted($file.path)) {
        throw 'Unsafe fixture path in manifest.'
    }
    $source = [IO.Path]::GetFullPath([IO.Path]::Combine($KitDirectory, $file.path))
    if (-not $source.StartsWith($localPrefix, [StringComparison]::OrdinalIgnoreCase) -or -not [IO.File]::Exists($source)) {
        throw 'A fixture resolves outside the kit or is missing.'
    }
    if ((Get-FixtureHash $source) -cne $file.sha256) { throw "Local fixture changed after validation: $($file.path)" }
}

$exists = (& $adbExecutable -s $Device shell "if [ -e $RemoteRoot ]; then echo exists; else echo new; fi" | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not check the Android destination.' }
if ($exists -eq 'exists') { $RemoteRoot += '-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6) }
& $adbExecutable -s $Device shell mkdir $RemoteRoot
if ($LASTEXITCODE -ne 0) { throw 'Could not create a fresh Android destination; existing files were not replaced.' }

foreach ($file in $files) {
    $source = [IO.Path]::Combine($KitDirectory, $file.path)
    $destination = "$RemoteRoot/$($file.path)"
    $parent = $destination.Substring(0, $destination.LastIndexOf('/'))
    & $adbExecutable -s $Device shell mkdir -p $parent
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the provider directory.' }
    & $adbExecutable -s $Device push $source $destination | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not copy $($file.path)." }
    $remoteHash = (& $adbExecutable -s $Device shell sha256sum $destination | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $remoteHash -notmatch '^(?<Hash>[0-9a-fA-F]{64})\s' -or $Matches.Hash.ToLowerInvariant() -cne $file.sha256) {
        throw "Android checksum verification failed: $($file.path)"
    }
    & $adbExecutable -s $Device shell am broadcast -a android.intent.action.MEDIA_SCANNER_SCAN_FILE -d "file://$destination" | Out-Null
}
Write-Output "Copied and SHA-256 verified $($files.Count) files / $(@($manifest.cases).Count) cases on ${Device}: $RemoteRoot"
Write-Output "Synthetic account prefix: $($manifest.prefix). Open START-HERE.txt and each provider's README.txt. No app data was accessed or imported."
