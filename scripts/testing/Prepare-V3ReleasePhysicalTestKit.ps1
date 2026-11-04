<#
.SYNOPSIS
Builds a local synthetic-data kit for v3 physical acceptance.

.DESCRIPTION
Copies reviewed synthetic fixtures, Google Authenticator transfer QR images, and the deterministic
OTP Harbor demo icon pack into an ignored artifacts directory. No encrypted vault or password is
included; those must be created by the tester on the target installation.
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = 'artifacts/testing/v3-release-physical'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$resolvedOutput = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
$testingRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts/testing'))
if (-not $resolvedOutput.StartsWith(
    $testingRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The physical-test kit output must remain below artifacts/testing.'
}
[IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

$sources = [ordered]@{
    'mixed-account-current-v3.json' = 'scripts/testing/fixtures/mixed-account-current-v3.json'
    'mixed-account-older.json' = 'scripts/testing/fixtures/mixed-account-older.json'
    'release-import-uris.txt' = 'scripts/testing/fixtures/release-import-uris.txt'
    'release-aegis.json' = 'scripts/testing/fixtures/release-aegis.json'
    'release-twofas.2fas' = 'scripts/testing/fixtures/release-twofas.2fas'
    'release-native.csv' = 'scripts/testing/fixtures/release-native.csv'
    'google-authenticator-10-account-export-1.png' = 'OtpHarbor.Tests/Fixtures/Qr/google-authenticator-10-account-export-1.png'
    'google-authenticator-10-account-export-2.png' = 'OtpHarbor.Tests/Fixtures/Qr/google-authenticator-10-account-export-2.png'
    'otp-harbor-demo-icons.otphicons' = 'artifacts/marketing/demo-data/otp-harbor-demo-icons.otphicons'
    'README.md' = 'docs/testing/V3_RELEASE_PHYSICAL_ACCEPTANCE.md'
}

$expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $sources.GetEnumerator()) {
    $source = Join-Path $repositoryRoot $entry.Value
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Physical-test source is missing: $($entry.Value)"
    }
    $destination = Join-Path $resolvedOutput $entry.Key
    Copy-Item -LiteralPath $source -Destination $destination -Force
    [void]$expected.Add([IO.Path]::GetFullPath($destination))
}

$hashLines = foreach ($name in $sources.Keys | Where-Object { $_ -ne 'README.md' } | Sort-Object) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $resolvedOutput $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name"
}
$hashPath = Join-Path $resolvedOutput 'SHA256SUMS'
[IO.File]::WriteAllLines($hashPath, $hashLines, [Text.UTF8Encoding]::new($false))
[void]$expected.Add([IO.Path]::GetFullPath($hashPath))

foreach ($file in Get-ChildItem -LiteralPath $resolvedOutput -File) {
    if (-not $expected.Contains([IO.Path]::GetFullPath($file.FullName))) {
        [IO.File]::Delete($file.FullName)
    }
}

Write-Output "Prepared synthetic v3 physical-test kit: $resolvedOutput"
Write-Output "Files: $($expected.Count)"
