#requires -Version 7.0
param([Parameter(Mandatory)][string]$ReleaseTag, [Parameter(Mandatory)][string]$SourceCommit, [switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Test-ReleaseSource.ps1') -ReleaseTag $ReleaseTag -SourceCommit $SourceCommit
$tag = "$ReleaseTag"
if ($tag -notmatch '^v(?<base>\d+\.\d+\.\d+)(?:-rc(?<rc>\d+))?$') {
  throw "Release tag has an unsupported format."
}
$baseVersion = $Matches.base
$rcNumber = $Matches.rc
$releaseVersion = if ($rcNumber) { "$baseVersion-rc$rcNumber" } else { $baseVersion }
"release_version=$releaseVersion" >> $env:GITHUB_OUTPUT
"prerelease=$(if ($rcNumber) { 'true' } else { 'false' })" >> $env:GITHUB_OUTPUT

$artifactPaths = @(Get-ChildItem release-assets -File | Where-Object {
  $_.Name -match '^(OTP-Harbor-linux-x64-.+\.tar\.gz|otp-harbor_.+_amd64\.deb)$'
} | Select-Object -ExpandProperty FullName)
if ($artifactPaths.Count -ne 2) {
  throw "The stable desktop GitHub release must contain exactly the two Linux artifacts."
}

./scripts/release/shared/New-ReleaseArtifactManifest.ps1 `
  -ReleaseVersion $releaseVersion `
  -SourceCommit "$SourceCommit" `
  -ArtifactPath $artifactPaths `
    -OutputPath release-assets/release-artifacts-v2.json `
    -ReleaseProfile $(if ($ValidateOnly) { 'unsigned-platform-preview' } else { 'signed' })
./scripts/release/shared/Test-ReleaseArtifactManifest.ps1 `
  -ManifestPath release-assets/release-artifacts-v2.json `
  -ArtifactDirectory release-assets

if ($ValidateOnly) { return }
try {
    if ([string]::IsNullOrWhiteSpace($env:NETSPARKLE_PUBLIC_KEY) -or
        [string]::IsNullOrWhiteSpace($env:NETSPARKLE_PRIVATE_KEY)) {
      throw "Ed25519 release signing credentials are required."
    }
    $clientSettings = Get-Content OtpHarbor.UI.Avalonia.Desktop/appsettings.json -Raw | ConvertFrom-Json
    $expectedPublicKey = $clientSettings.AutoUpdate.PublicKey
    if ($expectedPublicKey -cne $env:NETSPARKLE_PUBLIC_KEY.Trim()) {
      throw "The CI public key does not match the key embedded in the Avalonia client."
    }

    $keyDirectory = Join-Path $env:RUNNER_TEMP "netsparkle-keys"
    New-Item -ItemType Directory -Path $keyDirectory | Out-Null
    $publicKeyPath = Join-Path $keyDirectory "NetSparkle_Ed25519.pub"
    $privateKeyPath = Join-Path $keyDirectory "NetSparkle_Ed25519.priv"
    [IO.File]::WriteAllText($publicKeyPath, $env:NETSPARKLE_PUBLIC_KEY.Trim(), [Text.Encoding]::ASCII)
    [IO.File]::WriteAllText($privateKeyPath, $env:NETSPARKLE_PRIVATE_KEY.Trim(), [Text.Encoding]::ASCII)
    & chmod 600 $privateKeyPath

    $baseUrl = "https://github.com/$env:GITHUB_REPOSITORY/releases/download/$ReleaseTag/"
    ./scripts/release/shared/Generate-AvaloniaAppcast.ps1 `
      -ManifestPath release-assets/release-artifacts-v2.json `
      -ArtifactDirectory release-assets `
      -BaseDownloadUrl $baseUrl `
      -PrivateKeyPath $privateKeyPath `
      -PublicKeyPath $publicKeyPath `
      -ExpectedPublicKey $expectedPublicKey `
      -OutputDirectory release-feed
    ./scripts/release/shared/Test-AvaloniaAppcast.ps1 `
      -AppcastPath release-feed/appcast-v2.xml `
      -ManifestPath release-assets/release-artifacts-v2.json `
      -BaseDownloadUrl $baseUrl

    $feedSignature = (Get-Content release-feed/appcast-v2.xml.signature -Raw).Trim()
    netsparkle-generate-appcast `
      --verify release-feed/appcast-v2.xml `
      --signature $feedSignature `
      --key-path $keyDirectory
    if ($LASTEXITCODE -ne 0) { throw "Avalonia appcast signature verification failed." }

    $manifestSignature = (Get-Content release-assets/release-artifacts-v2.json.signature -Raw).Trim()
    netsparkle-generate-appcast `
      --verify release-assets/release-artifacts-v2.json `
      --signature $manifestSignature `
      --key-path $keyDirectory
    if ($LASTEXITCODE -ne 0) { throw "Artifact manifest signature verification failed." }

    Copy-Item release-feed/appcast-v2.xml release-assets/
    Copy-Item release-feed/appcast-v2.xml.signature release-assets/
}

finally {
    $cleanup = [IO.Path]::GetFullPath((Join-Path $env:RUNNER_TEMP 'netsparkle-keys'))
    $expectedRoot = [IO.Path]::GetFullPath($env:RUNNER_TEMP) + [IO.Path]::DirectorySeparatorChar
    if (-not $cleanup.StartsWith($expectedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Signing cleanup escaped the runner temporary directory.' }
    if (Test-Path -LiteralPath $cleanup) { Remove-Item -LiteralPath $cleanup -Recurse -Force }
}
