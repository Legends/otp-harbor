#requires -Version 7.0
<#
.SYNOPSIS
Rejects a stale runner Android workload before restoring or publishing release assets.
#>
$ErrorActionPreference = 'Stop'
$toolchainRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$toolchainPolicy = (Get-Content (Join-Path $toolchainRoot 'global.json') -Raw | ConvertFrom-Json).sdk
Push-Location $toolchainRoot
try {
    $toolchainSdk = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $toolchainSdk -ne $toolchainPolicy.version) { throw 'The active .NET SDK does not match global.json.' }
    $toolchainWorkload = (& dotnet workload --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $toolchainWorkload -ne $toolchainPolicy.workloadVersion) { throw 'The active workload set does not match global.json.' }
    $toolchainAndroidVersion = (& dotnet msbuild OtpHarbor.UI.Avalonia.Android/OtpHarbor.UI.Avalonia.Android.csproj -nologo -getProperty:AndroidNETSdkVersion).Trim()
    if ($LASTEXITCODE -ne 0 -or $toolchainAndroidVersion -ne '36.1.118') {
        throw 'The Android SDK must resolve to the validated .NET 10 pack (36.1.118).'
    }
    Write-Output "Validated .NET SDK $toolchainSdk, workload set $toolchainWorkload, Android SDK 36.1.118."
}
finally { Pop-Location }
