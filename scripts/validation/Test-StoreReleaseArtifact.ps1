Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$verifier = Join-Path $repositoryRoot 'scripts/release/windows/Save-StoreReleaseArtifact.ps1'
$testRoot = Join-Path $repositoryRoot ('artifacts/shared/testing/store-release-artifact-tests/' + [guid]::NewGuid().ToString('N'))
Add-Type -AssemblyName System.IO.Compression.FileSystem

function New-StoreFixture([string]$Name, [string]$ManifestName = 'Legends77.OTPHarbor') {
    $directory = Join-Path $testRoot $Name
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $package = 'OTP-Harbor_3-4-65535-0_x64.msix'
    $path = Join-Path $directory $package
    $archive = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $entry = $archive.CreateEntry('AppxManifest.xml')
        $writer = [IO.StreamWriter]::new($entry.Open())
        try { $writer.Write("<Package><Identity Name='$ManifestName' Version='3.4.65535.0' Publisher='CN=84095A7C-6458-436E-ABF2-DC02311E25F9' ProcessorArchitecture='x64'/></Package>") }
        finally { $writer.Dispose() }
    }
    finally { $archive.Dispose() }
    $hash = (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()
    $metadata = [ordered]@{ schemaVersion = 1; package = $package; identityName = 'Legends77.OTPHarbor'
        publisher = 'CN=84095A7C-6458-436E-ABF2-DC02311E25F9'; version = '3.4.65535.0'
        architecture = 'x64'; distribution = 'microsoft-store-only'; signed = $false; sha256 = $hash }
    $metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'store-package.json') -Encoding utf8
    "$hash  $package" | Set-Content -LiteralPath (Join-Path $directory 'SHA256SUMS') -Encoding utf8
    return $directory
}

$valid = New-StoreFixture 'valid path with spaces'
Push-Location ([IO.Path]::GetTempPath())
try { & $verifier -ReleaseTag v3.0.4 -ArtifactDirectory $valid -VerifyOnly }
finally { Pop-Location }

foreach ($case in @('hash', 'checksum', 'version', 'publisher', 'path', 'schema', 'signed', 'distribution', 'missing', 'identity')) {
    $directory = New-StoreFixture $case $(if ($case -eq 'identity') { 'Unrelated.Package' } else { 'Legends77.OTPHarbor' })
    $metadataPath = Join-Path $directory 'store-package.json'
    $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
    switch ($case) {
        hash { [IO.File]::AppendAllText((Join-Path $directory $metadata.package), 'changed') }
        checksum { 'invalid' | Set-Content -LiteralPath (Join-Path $directory 'SHA256SUMS') }
        version { $metadata.version = '3.3.65535.0' }
        publisher { $metadata.publisher = 'CN=Unrelated' }
        path { $metadata.package = '../outside.msix' }
        schema { $metadata.schemaVersion = 2 }
        signed { $metadata.signed = $true }
        distribution { $metadata.distribution = 'direct' }
        missing { Remove-Item -LiteralPath (Join-Path $directory $metadata.package) }
    }
    $metadata | ConvertTo-Json | Set-Content -LiteralPath $metadataPath -Encoding utf8
    $rejected = $false
    try { & $verifier -ReleaseTag v3.0.4 -ArtifactDirectory $directory -VerifyOnly | Out-Null }
    catch { $rejected = $true }
    if (-not $rejected) { throw "Store release verification accepted invalid $case fixture." }
}
Write-Output 'Store artifact verification passed: valid archive, working-directory independence, and 10 rejection cases. No network, installation or submission.'
