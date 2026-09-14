<#
.SYNOPSIS
Builds a Partner Center MSIX from the current OTP Harbor source tree.

.DESCRIPTION
Resolves the stable release tag and its Microsoft Store package version, then invokes the reviewed
Store packager with OTP Harbor's reserved Partner Center identity. By default the source tree must
be clean and an existing tag must point at HEAD. Use -ReleaseTag for a clean commit that has not
been tagged yet, or -AllowDirtySource for an intentional local test build.
#>
[CmdletBinding()]
param(
    [ValidatePattern('^v\d+\.\d+\.\d+$')]
    [string]$ReleaseTag,

    [string]$OutputDirectory,

    [switch]$AllowDirtySource
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsWindows) {
    throw 'Microsoft Store MSIX packages must be built on Windows with the Windows SDK installed.'
}

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$gitRoot = (& git -C $repositoryRoot rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($gitRoot)) {
    throw 'The repository root is not a Git worktree.'
}
if ([IO.Path]::GetFullPath($gitRoot.Trim()) -cne [IO.Path]::GetFullPath($repositoryRoot)) {
    throw 'The release helper resolved an unexpected Git worktree.'
}

$headCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Unable to resolve the current Git commit.' }

if ([string]::IsNullOrWhiteSpace($ReleaseTag)) {
    $stableTags = @(
        @(& git -C $repositoryRoot tag --points-at HEAD) |
            Where-Object { $_ -match '^v\d+\.\d+\.\d+$' }
    )
    if ($stableTags.Count -ne 1) {
        throw 'HEAD must have exactly one stable release tag, or pass -ReleaseTag v<major>.<minor>.<patch>.'
    }
    $ReleaseTag = $stableTags[0]
}

$tagCommit = & git -C $repositoryRoot rev-parse --verify "refs/tags/$ReleaseTag`^{commit}" 2>$null
$tagExists = $LASTEXITCODE -eq 0
if ($tagExists -and $tagCommit.Trim() -cne $headCommit) {
    throw "Release tag $ReleaseTag does not point at the current commit $headCommit."
}

$sourceStatus = @(& git -C $repositoryRoot status --porcelain --untracked-files=all)
$isDirty = $sourceStatus.Count -gt 0
if ($isDirty -and -not $AllowDirtySource) {
    throw 'The source tree has uncommitted changes. Commit them first or pass -AllowDirtySource for an intentional local test build.'
}

$versionResolver = Join-Path $PSScriptRoot 'Get-MicrosoftStoreVersion.ps1'
$storeVersion = (& $versionResolver -ReleaseTag $ReleaseTag).Trim()
$productVersion = $ReleaseTag.Substring(1)
$resolvedOutputDirectory = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    "artifacts/store/$ReleaseTag"
} else {
    $OutputDirectory
}

$packager = Join-Path $PSScriptRoot 'New-MicrosoftStoreMsix.ps1'
& $packager `
    -IdentityName 'Legends77.OTPHarbor' `
    -Publisher 'CN=84095A7C-6458-436E-ABF2-DC02311E25F9' `
    -PublisherDisplayName 'Legends77' `
    -Version $storeVersion `
    -ProductVersion $productVersion `
    -OutputDirectory $resolvedOutputDirectory

$absoluteOutput = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $resolvedOutputDirectory))
$metadataPath = Join-Path $absoluteOutput 'store-package.json'
if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) {
    throw 'The Store packager did not produce store-package.json.'
}

$metadata = [IO.File]::ReadAllText($metadataPath) | ConvertFrom-Json
$msixPath = Join-Path $absoluteOutput $metadata.package
if (-not (Test-Path -LiteralPath $msixPath -PathType Leaf)) {
    throw 'The Store packager did not produce the expected MSIX.'
}

$actualHash = (Get-FileHash -LiteralPath $msixPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -cne $metadata.sha256) {
    throw 'The generated MSIX does not match store-package.json.'
}

[pscustomobject]@{
    MsixPath = $msixPath
    ReleaseTag = $ReleaseTag
    StoreVersion = $storeVersion
    SourceCommit = $headCommit
    DirtySource = $isDirty
    Sha256 = $actualHash
}
