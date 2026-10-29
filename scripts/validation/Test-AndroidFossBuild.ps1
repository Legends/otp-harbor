<#
.SYNOPSIS
Validates deterministic and FOSS-oriented Android build inputs.

.DESCRIPTION
Checks the pinned .NET SDK, Android NuGet lock graph, configured package source, known proprietary
dependency families, CI locked-restore controls, and the explicit F-Droid readiness disclosure.
This is a repository guardrail, not a substitute for F-Droid's scanner or review.
#>
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))

function Read-RequiredFile([string]$RelativePath) {
    $path = Join-Path $repositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required Android FOSS build file is missing: $RelativePath"
    }
    [IO.File]::ReadAllText($path)
}

$sdkConfiguration = Read-RequiredFile 'global.json' | ConvertFrom-Json
if ($sdkConfiguration.sdk.version -cne '10.0.402' -or
    $sdkConfiguration.sdk.rollForward -cne 'disable' -or
    $sdkConfiguration.sdk.allowPrerelease -ne $false) {
    throw 'global.json must pin stable .NET SDK 10.0.402 with roll-forward disabled.'
}

$nugetConfiguration = [xml](Read-RequiredFile 'NuGet.config')
$packageSources = @($nugetConfiguration.configuration.packageSources.add)
if ($null -eq $nugetConfiguration.configuration.packageSources.clear -or
    $packageSources.Count -ne 1 -or
    [string]$packageSources[0].key -cne 'nuget.org' -or
    [string]$packageSources[0].value -cne 'https://api.nuget.org/v3/index.json') {
    throw 'NuGet.config must clear inherited sources and allow only the canonical nuget.org feed.'
}

$lockFiles = @(
    'OtpHarbor.Core\packages.lock.json',
    'OtpHarbor.DAL\packages.lock.json',
    'OtpHarbor.Infrastructure\packages.lock.json',
    'OtpHarbor.Platform.Android\packages.lock.json',
    'OtpHarbor.UI.Avalonia.Shared\packages.lock.json',
    'OtpHarbor.UI.Avalonia.Mobile\packages.lock.json',
    'OtpHarbor.UI.Avalonia.Android\packages.lock.json')
$expectedLockTargets = [ordered]@{
    'OtpHarbor.Core\packages.lock.json' = @('net10.0')
    'OtpHarbor.DAL\packages.lock.json' = @('net10.0')
    'OtpHarbor.Infrastructure\packages.lock.json' = @('net10.0')
    'OtpHarbor.Platform.Android\packages.lock.json' = @('net10.0-android36.0')
    'OtpHarbor.UI.Avalonia.Shared\packages.lock.json' = @('net10.0')
    'OtpHarbor.UI.Avalonia.Mobile\packages.lock.json' = @('net10.0')
    'OtpHarbor.UI.Avalonia.Android\packages.lock.json' = @(
        'net10.0-android36.0',
        'net10.0-android36.0/android-arm64',
        'net10.0-android36.0/android-x64')
}
$packageIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

foreach ($lockFile in $lockFiles) {
    $lock = Read-RequiredFile $lockFile | ConvertFrom-Json -AsHashtable
    if ([int]$lock.version -ne 1 -or $lock.dependencies.Count -eq 0) {
        throw "Android dependency lock file is empty or unsupported: $lockFile"
    }

    $actualTargets = @($lock.dependencies.Keys | Sort-Object)
    $expectedTargets = @($expectedLockTargets[$lockFile] | Sort-Object)
    if ([string]::Join('|', $actualTargets) -cne [string]::Join('|', $expectedTargets)) {
        throw "Android dependency lock file has stale or incomplete runtime targets: $lockFile. Expected '$([string]::Join(', ', $expectedTargets))'; found '$([string]::Join(', ', $actualTargets))'."
    }

    foreach ($framework in $lock.dependencies.Values) {
        foreach ($dependency in $framework.GetEnumerator()) {
            $details = $dependency.Value
            if ([string]$details.type -eq 'Project') { continue }
            if ([string]::IsNullOrWhiteSpace([string]$details.resolved) -or
                [string]::IsNullOrWhiteSpace([string]$details.contentHash)) {
                throw "Locked package '$($dependency.Key)' in '$lockFile' lacks a version or content hash."
            }
            $null = $packageIds.Add([string]$dependency.Key)
        }
    }
}

foreach ($localRidBuildScript in @(
    'scripts\testing\Install-AndroidDevelopmentBuild.ps1',
    'scripts\dev\Publish-And-Run-AvaloniaWindows.ps1')) {
    $localRidBuild = Read-RequiredFile $localRidBuildScript
    foreach ($requiredLockIsolationControl in @(
        'NuGetLockFilePath',
        '$(MSBuildProjectDirectory)/obj/otp-harbor-local-runtime.lock.json')) {
        if (-not $localRidBuild.Contains($requiredLockIsolationControl, [StringComparison]::Ordinal)) {
            throw "RID-specific local build can rewrite canonical dependency locks: $localRidBuildScript"
        }
    }
}

$prohibitedPackagePatterns = @(
    '^Xamarin\.GooglePlayServices(?:\.|$)',
    '^Xamarin\.Firebase(?:\.|$)',
    '^Google\.Android\.Play(?:\.|$)',
    '^Microsoft\.AppCenter(?:\.|$)',
    '^Sentry(?:\.|$)',
    '^Xamarin\.Facebook(?:\.|$)',
    '^Adjust(?:\.|$)',
    '^AppsFlyer(?:\.|$)',
    '^Branch\.Xamarin(?:\.|$)')
foreach ($packageId in $packageIds) {
    foreach ($pattern in $prohibitedPackagePatterns) {
        if ($packageId -match $pattern) {
            throw "Android dependency graph contains prohibited package '$packageId'."
        }
    }
}

$workflow = Read-RequiredFile '.github\workflows\build-and-test.yml'
foreach ($required in @(
    'dotnet-version: "10.0.402"',
    'dotnet restore OtpHarbor.Android.sln --locked-mode',
    'dotnet restore OtpHarbor.UI.Avalonia.Android/OtpHarbor.UI.Avalonia.Android.csproj --locked-mode',
    'dotnet workload install android --skip-manifest-update',
    './scripts/validation/Test-AndroidFossBuild.ps1',
    './scripts/validation/Test-AndroidApkPayloadComparison.ps1',
    './scripts/validation/Test-AndroidDistributionChannels.ps1',
    './scripts/validation/Test-AndroidDependencyLicenses.ps1')) {
    if (-not $workflow.Contains($required, [StringComparison]::Ordinal)) {
        throw "Android workflow is missing deterministic build control: $required"
    }
}

$readiness = Read-RequiredFile 'docs\android\FDROID_READINESS.md'
foreach ($required in @(
    'not currently available from the official F-Droid repository',
    'F-Droid build-server-compatible toolchain',
    'Reproducible APK evidence',
    'dotnet restore OtpHarbor.Android.sln --locked-mode')) {
    if (-not $readiness.Contains($required, [StringComparison]::OrdinalIgnoreCase)) {
        throw "F-Droid readiness documentation is missing required disclosure: $required"
    }
}

Write-Output "Android FOSS build inputs are locked across $($lockFiles.Count) projects and $($packageIds.Count) packages."
