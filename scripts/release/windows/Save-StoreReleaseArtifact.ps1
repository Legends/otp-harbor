<#
.SYNOPSIS
Retains the non-public Partner Center output of a successful desktop tag build locally.
.DESCRIPTION
Downloads only the Store CI artifact, verifies its identity/version and byte hashes,
and saves it under artifacts/windows/release/<tag>. Never installs or submits it.
VerifyOnly performs offline checks without GitHub or Git access.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^v\d+\.\d+\.\d+$')][string]$ReleaseTag,
    [ValidateRange(0, [long]::MaxValue)][long]$RunId = 0,
    [switch]$VerifyOnly,
    [string]$ArtifactDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../..')).Path
$canonicalDirectory = Join-Path $repositoryRoot "artifacts/windows/release/$ReleaseTag"
if (-not $ArtifactDirectory) { $ArtifactDirectory = $canonicalDirectory }
$ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
if (-not $VerifyOnly -and $ArtifactDirectory -ne [IO.Path]::GetFullPath($canonicalDirectory)) {
    throw 'Downloads must use the canonical local release directory.'
}
if ((Test-Path -LiteralPath $ArtifactDirectory) -and
    ((Get-Item -LiteralPath $ArtifactDirectory).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw 'Release output must not be a directory link.'
}

if (-not $VerifyOnly -and -not (Test-Path -LiteralPath (Join-Path $ArtifactDirectory 'store-package.json'))) {
    $expectedCommit = (& git -C $repositoryRoot rev-parse "$ReleaseTag^{commit}" 2>$null)
    if ($LASTEXITCODE -ne 0 -or $expectedCommit -notmatch '^[0-9a-f]{40}$') {
        throw 'Fetch the desktop release tag before retrieving its Store artifact.'
    }
    if ($RunId -eq 0) {
        $runs = @(gh run list --repo Legends/otp-harbor --workflow build-and-test.yml --branch $ReleaseTag --status success --limit 10 --json databaseId,headSha | ConvertFrom-Json)
        if ($LASTEXITCODE -ne 0) { throw 'Could not find the successful release workflow.' }
        $matching = @($runs | Where-Object headSha -EQ $expectedCommit)
        if ($matching.Count -eq 0) { throw 'No successful release build matches the immutable tag.' }
        $RunId = $matching[0].databaseId
    }
    $run = gh api "repos/Legends/otp-harbor/actions/runs/$RunId" | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $run.conclusion -cne 'success' -or $run.head_sha -cne $expectedCommit -or
        $run.head_branch -cne $ReleaseTag -or $run.path -cne '.github/workflows/build-and-test.yml') {
        throw 'The workflow is not a successful build of this desktop release tag.'
    }
    New-Item -ItemType Directory -Path $ArtifactDirectory -Force | Out-Null
    gh run download $RunId --repo Legends/otp-harbor --name "otp-harbor-microsoft-store-$($ReleaseTag.Substring(1))" --dir $ArtifactDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Store artifact download failed. Retained files may be incomplete; retry into an empty release directory.' }
    [ordered]@{ releaseTag = $ReleaseTag; sourceCommit = $expectedCommit; workflowRunId = $RunId
        workflowUrl = $run.html_url; downloadedAtUtc = [DateTimeOffset]::UtcNow.ToString('O') } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'release-origin.json') -Encoding utf8
}

$metadata = Get-Content -LiteralPath (Join-Path $ArtifactDirectory 'store-package.json') -Raw | ConvertFrom-Json
$expectedVersion = (& (Join-Path $PSScriptRoot 'Get-MicrosoftStoreVersion.ps1') -ReleaseTag $ReleaseTag).Trim()
if ($metadata.schemaVersion -ne 1 -or $metadata.identityName -cne 'Legends77.OTPHarbor' -or
    $metadata.publisher -cne 'CN=84095A7C-6458-436E-ABF2-DC02311E25F9' -or
    $metadata.version -cne $expectedVersion -or $metadata.architecture -cne 'x64' -or
    $metadata.distribution -cne 'microsoft-store-only' -or $metadata.signed -ne $false -or
    $metadata.package -notmatch '^OTP-Harbor_[A-Za-z0-9-]+_x64\.msix$' -or
    $metadata.sha256 -notmatch '^[0-9a-f]{64}$') { throw 'Store artifact metadata does not match the release.' }
$package = Join-Path $ArtifactDirectory $metadata.package
if (-not (Test-Path -LiteralPath $package -PathType Leaf) -or
    ((Get-Item -LiteralPath $package).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw 'The MSIX must be a regular file in the release directory.'
}
$hash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
$checksum = (Get-Content -LiteralPath (Join-Path $ArtifactDirectory 'SHA256SUMS') -Raw).Trim()
if ($hash -cne $metadata.sha256 -or $checksum -cne "$hash  $($metadata.package)") {
    throw 'The downloaded MSIX does not match its integrity metadata.'
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($package)
try {
    $entry = $archive.GetEntry('AppxManifest.xml')
    if ($null -eq $entry -or $entry.Length -gt 262144) { throw 'The MSIX manifest is missing or too large.' }
    $stream = $entry.Open()
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.MaxCharactersInDocument = 262144
    $reader = [Xml.XmlReader]::Create($stream, $settings)
    try { $xml = [Xml.XmlDocument]::new(); $xml.Load($reader) }
    finally { $reader.Dispose(); $stream.Dispose() }
    if ($xml.Package.Identity.Name -cne $metadata.identityName -or
        $xml.Package.Identity.Version -cne $metadata.version -or
        $xml.Package.Identity.Publisher -cne $metadata.publisher -or
        $xml.Package.Identity.ProcessorArchitecture -cne 'x64') { throw 'The MSIX identity does not match its metadata.' }
}
finally { $archive.Dispose() }
Write-Output "Verified Partner Center release output: $ArtifactDirectory"
