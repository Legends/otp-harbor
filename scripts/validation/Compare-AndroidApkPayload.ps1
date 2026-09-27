<#
.SYNOPSIS
Compares the non-signature payload of two Android APK files.

.DESCRIPTION
Hashes every ZIP entry except Android/JAR signature records below META-INF. ZIP entry order,
timestamps, compression, and APK signing blocks are intentionally outside this payload comparison.
A match is evidence of payload equivalence, not proof of a fully reproducible build.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ReferenceApk,

    [Parameter(Mandatory)]
    [string]$CandidateApk,

    [string]$ReportPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-IsSignatureEntry([string]$EntryName) {
    if (-not $EntryName.StartsWith('META-INF/', [StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    $leafName = [IO.Path]::GetFileName($EntryName)
    return $leafName.Equals('MANIFEST.MF', [StringComparison]::OrdinalIgnoreCase) -or
        $leafName.EndsWith('.SF', [StringComparison]::OrdinalIgnoreCase) -or
        $leafName.EndsWith('.RSA', [StringComparison]::OrdinalIgnoreCase) -or
        $leafName.EndsWith('.DSA', [StringComparison]::OrdinalIgnoreCase) -or
        $leafName.EndsWith('.EC', [StringComparison]::OrdinalIgnoreCase)
}

function Get-ApkPayload([string]$ApkPath) {
    $resolvedPath = [IO.Path]::GetFullPath($ApkPath)
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
        throw "APK does not exist: $resolvedPath"
    }
    if ([IO.Path]::GetExtension($resolvedPath) -cne '.apk') {
        throw "APK payload comparison requires an .apk file: $resolvedPath"
    }

    $payload = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $fileStream = [IO.File]::OpenRead($resolvedPath)
    try {
        $archive = [IO.Compression.ZipArchive]::new(
            $fileStream,
            [IO.Compression.ZipArchiveMode]::Read,
            $false)
        try {
            foreach ($entry in $archive.Entries) {
                if (Test-IsSignatureEntry $entry.FullName) { continue }
                if ($payload.ContainsKey($entry.FullName)) {
                    throw "APK contains a duplicate payload entry: $($entry.FullName)"
                }

                $entryStream = $entry.Open()
                try {
                    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($entryStream))
                }
                finally {
                    $entryStream.Dispose()
                }
                $payload.Add($entry.FullName, [pscustomobject]@{
                    Length = $entry.Length
                    Sha256 = $hash
                })
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    catch [IO.InvalidDataException] {
        throw "APK is not a readable ZIP archive: $resolvedPath"
    }
    finally {
        $fileStream.Dispose()
    }

    if ($payload.Count -eq 0) {
        throw "APK contains no comparable payload entries: $resolvedPath"
    }
    return $payload
}

function Get-ArtifactRecord([string]$ApkPath, [int]$PayloadEntryCount) {
    $resolvedPath = [IO.Path]::GetFullPath($ApkPath)
    $stream = [IO.File]::OpenRead($resolvedPath)
    try {
        $sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))
    }
    finally {
        $stream.Dispose()
    }

    $file = [IO.FileInfo]::new($resolvedPath)
    return [ordered]@{
        fileName = $file.Name
        byteLength = $file.Length
        sha256 = $sha256
        payloadEntryCount = $PayloadEntryCount
    }
}

function Write-ComparisonReport(
    [string]$Path,
    [Collections.Generic.Dictionary[string, object]]$ReferencePayload,
    [Collections.Generic.Dictionary[string, object]]$CandidatePayload,
    [string[]]$SortedDifferences) {
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $resolvedReference = [IO.Path]::GetFullPath($ReferenceApk)
    $resolvedCandidate = [IO.Path]::GetFullPath($CandidateApk)
    if ($resolvedPath.Equals($resolvedReference, [StringComparison]::OrdinalIgnoreCase) -or
        $resolvedPath.Equals($resolvedCandidate, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The comparison report must not overwrite an APK input.'
    }

    $parent = [IO.Path]::GetDirectoryName($resolvedPath)
    if ([string]::IsNullOrWhiteSpace($parent) -or -not [IO.Directory]::Exists($parent)) {
        throw "Comparison report directory does not exist: $parent"
    }

    $maximumReportedDifferences = 200
    $reportedDifferences = @($SortedDifferences | Select-Object -First $maximumReportedDifferences)
    $report = [ordered]@{
        schemaVersion = 1
        referenceArtifact = Get-ArtifactRecord $ReferenceApk $ReferencePayload.Count
        candidateArtifact = Get-ArtifactRecord $CandidateApk $CandidatePayload.Count
        payloadMatch = $SortedDifferences.Count -eq 0
        differenceCount = $SortedDifferences.Count
        reportedDifferences = $reportedDifferences
        differencesTruncated = $SortedDifferences.Count -gt $reportedDifferences.Count
    }
    $json = $report | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText($resolvedPath, $json, [Text.UTF8Encoding]::new($false))
}

$referencePayload = Get-ApkPayload $ReferenceApk
$candidatePayload = Get-ApkPayload $CandidateApk
$differences = [Collections.Generic.List[string]]::new()

foreach ($entryName in $referencePayload.Keys) {
    if (-not $candidatePayload.ContainsKey($entryName)) {
        $differences.Add("missing from candidate: $entryName")
        continue
    }

    $referenceEntry = $referencePayload[$entryName]
    $candidateEntry = $candidatePayload[$entryName]
    if ($referenceEntry.Length -ne $candidateEntry.Length -or
        $referenceEntry.Sha256 -cne $candidateEntry.Sha256) {
        $differences.Add("content differs: $entryName")
    }
}
foreach ($entryName in $candidatePayload.Keys) {
    if (-not $referencePayload.ContainsKey($entryName)) {
        $differences.Add("unexpected in candidate: $entryName")
    }
}
$sortedDifferences = @($differences | Sort-Object)

if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
    Write-ComparisonReport $ReportPath $referencePayload $candidatePayload $sortedDifferences
}

if ($differences.Count -gt 0) {
    $reported = @($sortedDifferences | Select-Object -First 20)
    $suffix = if ($differences.Count -gt $reported.Count) {
        "`n... and $($differences.Count - $reported.Count) more difference(s)."
    } else { '' }
    throw "Android APK payloads differ:`n$($reported -join "`n")$suffix"
}

Write-Output "Android APK payloads match across $($referencePayload.Count) non-signature entries."
