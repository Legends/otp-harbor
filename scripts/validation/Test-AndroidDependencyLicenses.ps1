<#
.SYNOPSIS
Validates licenses and cached package hashes for the locked Android NuGet graph.

.DESCRIPTION
Requires a completed locked restore. Package license declarations are useful review evidence but do
not prove that F-Droid will accept a prebuilt dependency or its source provenance.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))

$lockFiles = @(
    'TOTP.Core\packages.lock.json',
    'TOTP.DAL\packages.lock.json',
    'TOTP.Infrastructure\packages.lock.json',
    'TOTP.Platform.Android\packages.lock.json',
    'TOTP.UI.Avalonia.Shared\packages.lock.json',
    'TOTP.UI.Avalonia.Mobile\packages.lock.json',
    'TOTP.UI.Avalonia.Android\packages.lock.json')
$packages = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)

foreach ($lockFile in $lockFiles) {
    $path = Join-Path $repositoryRoot $lockFile
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Android dependency lock file is missing: $lockFile"
    }
    $lock = [IO.File]::ReadAllText($path) | ConvertFrom-Json -AsHashtable
    foreach ($framework in $lock.dependencies.Values) {
        foreach ($dependency in $framework.GetEnumerator()) {
            $details = $dependency.Value
            if ([string]$details.type -eq 'Project') { continue }
            $key = "$($dependency.Key)|$($details.resolved)"
            if ($packages.ContainsKey($key) -and
                $packages[$key].ContentHash -cne [string]$details.contentHash) {
                throw "Locked package '$key' has inconsistent content hashes."
            }
            $packages[$key] = [pscustomobject]@{
                Id = [string]$dependency.Key
                Version = [string]$details.resolved
                ContentHash = [string]$details.contentHash
            }
        }
    }
}

$packageRootOutput = @(& dotnet nuget locals global-packages --list 2>&1)
$packageRootLines = @($packageRootOutput | Where-Object {
    ([string]$_).StartsWith('global-packages:', [StringComparison]::OrdinalIgnoreCase)
})
if ($LASTEXITCODE -ne 0 -or $packageRootLines.Count -ne 1) {
    throw 'Unable to resolve the NuGet global-packages directory.'
}
$packageRoot = ([string]$packageRootLines[0] -replace '^global-packages:\s*', '').Trim()
$approvedExpressions = [Collections.Generic.HashSet[string]]::new(
    [string[]]@('MIT', 'Apache-2.0', 'MIT AND Apache-2.0', 'ISC'),
    [StringComparer]::Ordinal)
$licenseCounts = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)

foreach ($package in ($packages.Values | Sort-Object Id, Version)) {
    $packageDirectory = Join-Path (Join-Path $packageRoot $package.Id.ToLowerInvariant()) `
        $package.Version.ToLowerInvariant()
    if (-not (Test-Path -LiteralPath $packageDirectory -PathType Container)) {
        throw "Locked package is absent from the restored NuGet cache: $($package.Id) $($package.Version)"
    }

    $metadataPath = Join-Path $packageDirectory '.nupkg.metadata'
    if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) {
        throw "Cached NuGet package metadata is missing: $($package.Id) $($package.Version)"
    }
    $packageMetadata = [IO.File]::ReadAllText($metadataPath) | ConvertFrom-Json
    if ([string]$packageMetadata.contentHash -cne $package.ContentHash -or
        [string]$packageMetadata.source -cne 'https://api.nuget.org/v3/index.json') {
        throw "Cached NuGet metadata does not match the lock hash and canonical source: $($package.Id) $($package.Version)"
    }

    $packageBaseName = "$($package.Id.ToLowerInvariant()).$($package.Version.ToLowerInvariant()).nupkg"
    $archivePath = Join-Path $packageDirectory $packageBaseName
    $hashPath = "$archivePath.sha512"
    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $hashPath -PathType Leaf)) {
        throw "Cached NuGet archive or archive hash is missing: $($package.Id) $($package.Version)"
    }
    $archiveStream = [IO.File]::OpenRead($archivePath)
    try {
        $archiveHash = [Convert]::ToBase64String([Security.Cryptography.SHA512]::HashData($archiveStream))
    }
    finally {
        $archiveStream.Dispose()
    }
    if ($archiveHash -cne ([IO.File]::ReadAllText($hashPath)).Trim()) {
        throw "Cached NuGet archive does not match its SHA-512 record: $($package.Id) $($package.Version)"
    }

    $nuspecFiles = @(Get-ChildItem -LiteralPath $packageDirectory -File -Filter '*.nuspec')
    if ($nuspecFiles.Count -ne 1) {
        throw "Expected exactly one nuspec for locked package: $($package.Id) $($package.Version)"
    }
    [xml]$nuspec = [IO.File]::ReadAllText($nuspecFiles[0].FullName)
    $license = $nuspec.package.metadata.license
    if ($null -eq $license) {
        throw "Locked package has no NuGet license declaration: $($package.Id) $($package.Version)"
    }

    $licenseValue = $license.InnerText.Trim()
    if ([string]$license.type -ceq 'expression') {
        if (-not $approvedExpressions.Contains($licenseValue)) {
            throw "Locked package declares an unreviewed license expression '$licenseValue': $($package.Id) $($package.Version)"
        }
    }
    elseif ([string]$license.type -ceq 'file' -and
        $package.Id -ceq 'Otp.NET' -and
        $package.Version -ceq '1.4.1' -and
        $licenseValue -ceq 'LICENSE.txt') {
        $licensePath = Join-Path $packageDirectory $licenseValue
        $licenseHash = (Get-FileHash -LiteralPath $licensePath -Algorithm SHA256).Hash
        if ($licenseHash -cne '0BC32BE0CE13330BA88D208548582A641F95B1F66BB19BE91F5ACC1436C52472') {
            throw 'Otp.NET license file no longer matches the reviewed MIT text.'
        }
        $licenseValue = 'MIT (reviewed package file)'
    }
    else {
        throw "Locked package uses an unreviewed license declaration '$licenseValue': $($package.Id) $($package.Version)"
    }

    if (-not $licenseCounts.TryAdd($licenseValue, 1)) {
        $licenseCounts[$licenseValue]++
    }
}

$expectedLicenseCounts = [ordered]@{
    'MIT AND Apache-2.0' = 60
    'MIT' = 35
    'Apache-2.0' = 11
    'ISC' = 1
    'MIT (reviewed package file)' = 1
}
if ($licenseCounts.Count -ne $expectedLicenseCounts.Count) {
    throw 'Android dependency license inventory contains an unreviewed category.'
}
foreach ($expected in $expectedLicenseCounts.GetEnumerator()) {
    if (-not $licenseCounts.ContainsKey($expected.Key) -or
        $licenseCounts[$expected.Key] -ne $expected.Value) {
        throw "Android dependency license count changed for '$($expected.Key)'; review and update the audit."
    }
}

$audit = [IO.File]::ReadAllText((Join-Path $repositoryRoot 'docs\android\FDROID_DEPENDENCY_AUDIT.md'))
foreach ($required in @(
    "$($packages.Count) locked package ID/version pairs",
    '60 `MIT AND Apache-2.0`',
    '35 `MIT`',
    '11 `Apache-2.0`',
    '1 `ISC`',
    '1 reviewed MIT license file')) {
    if (-not $audit.Contains($required, [StringComparison]::Ordinal)) {
        throw "F-Droid dependency audit is out of sync: $required"
    }
}

Write-Output "Android NuGet audit verified $($packages.Count) locked package versions and their declared FLOSS licenses."
