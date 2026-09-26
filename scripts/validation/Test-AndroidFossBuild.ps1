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
if ($sdkConfiguration.sdk.version -cne '10.0.401' -or
    $sdkConfiguration.sdk.rollForward -cne 'disable' -or
    $sdkConfiguration.sdk.allowPrerelease -ne $false) {
    throw 'global.json must pin stable .NET SDK 10.0.401 with roll-forward disabled.'
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
    'TOTP.Core\packages.lock.json',
    'TOTP.DAL\packages.lock.json',
    'TOTP.Infrastructure\packages.lock.json',
    'TOTP.Platform.Android\packages.lock.json',
    'TOTP.UI.Avalonia.Shared\packages.lock.json',
    'TOTP.UI.Avalonia.Mobile\packages.lock.json',
    'TOTP.UI.Avalonia.Android\packages.lock.json')
$packageIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

foreach ($lockFile in $lockFiles) {
    $lock = Read-RequiredFile $lockFile | ConvertFrom-Json -AsHashtable
    if ([int]$lock.version -ne 1 -or $lock.dependencies.Count -eq 0) {
        throw "Android dependency lock file is empty or unsupported: $lockFile"
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
    'dotnet-version: "10.0.401"',
    'dotnet restore TOTP.Android.sln --locked-mode',
    'dotnet restore TOTP.UI.Avalonia.Android/TOTP.UI.Avalonia.Android.csproj --locked-mode',
    'dotnet workload install android --skip-manifest-update',
    './scripts/validation/Test-AndroidFossBuild.ps1',
    './scripts/validation/Test-AndroidApkPayloadComparison.ps1')) {
    if (-not $workflow.Contains($required, [StringComparison]::Ordinal)) {
        throw "Android workflow is missing deterministic build control: $required"
    }
}

$readiness = Read-RequiredFile 'docs\android\FDROID_READINESS.md'
foreach ($required in @(
    'not currently available from the official F-Droid repository',
    'F-Droid build-server-compatible toolchain',
    'Reproducible APK evidence',
    'dotnet restore TOTP.Android.sln --locked-mode')) {
    if (-not $readiness.Contains($required, [StringComparison]::OrdinalIgnoreCase)) {
        throw "F-Droid readiness documentation is missing required disclosure: $required"
    }
}

Write-Output "Android FOSS build inputs are locked across $($lockFiles.Count) projects and $($packageIds.Count) packages."
