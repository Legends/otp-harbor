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
    [string]$CandidateApk
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

if ($differences.Count -gt 0) {
    $reported = @($differences | Sort-Object | Select-Object -First 20)
    $suffix = if ($differences.Count -gt $reported.Count) {
        "`n... and $($differences.Count - $reported.Count) more difference(s)."
    } else { '' }
    throw "Android APK payloads differ:`n$($reported -join "`n")$suffix"
}

Write-Output "Android APK payloads match across $($referencePayload.Count) non-signature entries."
