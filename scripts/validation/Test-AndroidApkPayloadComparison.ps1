<#
.SYNOPSIS
Regression-tests the Android APK payload comparator with deterministic synthetic archives.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$comparisonScript = Join-Path $repositoryRoot 'scripts\validation\Compare-AndroidApkPayload.ps1'
$systemTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testRoot = Join-Path $systemTempRoot "otp-harbor-apk-compare-$([Guid]::NewGuid().ToString('N'))"

function New-TestApk(
    [string]$Path,
    [Collections.Specialized.OrderedDictionary]$Entries,
    [DateTimeOffset]$Timestamp,
    [IO.Compression.CompressionLevel]$CompressionLevel) {
    $stream = [IO.File]::Create($Path)
    try {
        $archive = [IO.Compression.ZipArchive]::new(
            $stream,
            [IO.Compression.ZipArchiveMode]::Create,
            $false)
        try {
            foreach ($item in $Entries.GetEnumerator()) {
                $entry = $archive.CreateEntry([string]$item.Key, $CompressionLevel)
                $entry.LastWriteTime = $Timestamp
                $entryStream = $entry.Open()
                try {
                    $bytes = [Text.Encoding]::UTF8.GetBytes([string]$item.Value)
                    $entryStream.Write($bytes, 0, $bytes.Length)
                }
                finally {
                    $entryStream.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-ComparisonFails(
    [string]$Reference,
    [string]$Candidate,
    [string]$ExpectedText,
    [string]$ReportPath = '') {
    try {
        if ([string]::IsNullOrWhiteSpace($ReportPath)) {
            & $comparisonScript -ReferenceApk $Reference -CandidateApk $Candidate | Out-Null
        } else {
            & $comparisonScript -ReferenceApk $Reference -CandidateApk $Candidate -ReportPath $ReportPath | Out-Null
        }
    }
    catch {
        if ($_.Exception.Message.Contains($ExpectedText, [StringComparison]::Ordinal)) { return }
        throw
    }
    throw "APK comparator accepted an invalid fixture; expected: $ExpectedText"
}

function New-DuplicateEntryApk([string]$Path) {
    $stream = [IO.File]::Create($Path)
    try {
        $archive = [IO.Compression.ZipArchive]::new(
            $stream,
            [IO.Compression.ZipArchiveMode]::Create,
            $false)
        try {
            foreach ($content in @('first', 'second')) {
                $entry = $archive.CreateEntry('classes.dex')
                $entryStream = $entry.Open()
                try {
                    $bytes = [Text.Encoding]::UTF8.GetBytes($content)
                    $entryStream.Write($bytes, 0, $bytes.Length)
                }
                finally {
                    $entryStream.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

[IO.Directory]::CreateDirectory($testRoot) | Out-Null
try {
    $reference = Join-Path $testRoot 'reference.apk'
    $equivalent = Join-Path $testRoot 'equivalent.apk'
    $different = Join-Path $testRoot 'different.apk'
    $missing = Join-Path $testRoot 'missing.apk'
    $duplicate = Join-Path $testRoot 'duplicate.apk'
    $matchReport = Join-Path $testRoot 'match-report.json'
    $differenceReport = Join-Path $testRoot 'difference-report.json'

    New-TestApk $reference ([ordered]@{
        'AndroidManifest.xml' = 'manifest'
        'classes.dex' = 'bytecode'
        'META-INF/MANIFEST.MF' = 'reference manifest digest'
        'META-INF/OTP.SF' = 'reference signature metadata'
        'META-INF/OTP.RSA' = 'reference signature'
    }) ([DateTimeOffset]'2024-01-01T00:00:00Z') ([IO.Compression.CompressionLevel]::Optimal)
    New-TestApk $equivalent ([ordered]@{
        'META-INF/OTHER.EC' = 'different signature'
        'classes.dex' = 'bytecode'
        'AndroidManifest.xml' = 'manifest'
    }) ([DateTimeOffset]'2025-06-01T00:00:00Z') ([IO.Compression.CompressionLevel]::NoCompression)
    New-TestApk $different ([ordered]@{
        'AndroidManifest.xml' = 'manifest'
        'classes.dex' = 'changed bytecode'
    }) ([DateTimeOffset]'2024-01-01T00:00:00Z') ([IO.Compression.CompressionLevel]::Optimal)
    New-TestApk $missing ([ordered]@{
        'AndroidManifest.xml' = 'manifest'
    }) ([DateTimeOffset]'2024-01-01T00:00:00Z') ([IO.Compression.CompressionLevel]::Optimal)
    New-DuplicateEntryApk $duplicate

    & $comparisonScript -ReferenceApk $reference -CandidateApk $equivalent -ReportPath $matchReport | Out-Null
    $matchEvidence = Get-Content -LiteralPath $matchReport -Raw | ConvertFrom-Json
    if ($matchEvidence.schemaVersion -ne 1 -or -not $matchEvidence.payloadMatch -or
        $matchEvidence.differenceCount -ne 0 -or $matchEvidence.differencesTruncated) {
        throw 'APK comparator match report did not record the expected result.'
    }
    if ($matchEvidence.referenceArtifact.fileName -ne 'reference.apk' -or
        $matchEvidence.candidateArtifact.fileName -ne 'equivalent.apk' -or
        $matchEvidence.referenceArtifact.payloadEntryCount -ne 2 -or
        $matchEvidence.referenceArtifact.sha256 -notmatch '^[0-9A-F]{64}$') {
        throw 'APK comparator match report did not record bounded artifact identity.'
    }
    if ((Get-Content -LiteralPath $matchReport -Raw).Contains($testRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'APK comparator report leaked an artifact path.'
    }

    Assert-ComparisonFails $reference $different 'content differs: classes.dex' $differenceReport
    $differenceEvidence = Get-Content -LiteralPath $differenceReport -Raw | ConvertFrom-Json
    if ($differenceEvidence.payloadMatch -or $differenceEvidence.differenceCount -ne 1 -or
        $differenceEvidence.reportedDifferences[0] -ne 'content differs: classes.dex') {
        throw 'APK comparator difference report did not record the payload drift.'
    }
    Assert-ComparisonFails $reference $missing 'missing from candidate: classes.dex'
    Assert-ComparisonFails $reference $duplicate 'duplicate payload entry: classes.dex'
    Assert-ComparisonFails $reference $equivalent 'must not overwrite an APK input' $reference
}
finally {
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    if (-not $resolvedTestRoot.StartsWith($systemTempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedTestRoot) -notmatch '^otp-harbor-apk-compare-[0-9a-f]{32}$') {
        throw "Refusing to remove unexpected APK comparison test path: $resolvedTestRoot"
    }
    Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
}

Write-Output 'Android APK payload comparator rejects content and entry-set drift while ignoring packaging and signatures.'
