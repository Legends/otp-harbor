#requires -Version 7.0
param([Parameter(Mandatory)][string]$ReleaseTag, [Parameter(Mandatory)][string]$SourceCommit)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Test-ReleaseSource.ps1') -ReleaseTag $ReleaseTag -SourceCommit $SourceCommit
$tag = "$ReleaseTag"
if ($tag -notmatch '^android-v(?<version>\d+\.\d+\.\d+)$') {
  throw "Android release tag must match android-v<major>.<minor>.<patch>."
}
$releaseVersion = $Matches.version
"release_version=$releaseVersion" >> $env:GITHUB_OUTPUT

$apkPaths = @(Get-ChildItem android-release-assets -File -Filter '*.apk')
if ($apkPaths.Count -ne 1 -or
    $apkPaths[0].Name -cne "OTP-Harbor-android-universal-$releaseVersion.apk") {
  throw "The Android release must contain exactly one correctly versioned APK."
}
$metadataPath = Join-Path $PWD 'android-release-assets/android-release.json'
if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) {
  throw "The verified Android release metadata is missing."
}

./scripts/release/shared/New-ReleaseArtifactManifest.ps1 `
  -ReleaseVersion $releaseVersion `
  -SourceCommit "$SourceCommit" `
  -ArtifactPath $apkPaths[0].FullName `
  -OutputPath android-release-assets/release-artifacts-v2.json
./scripts/release/shared/Test-ReleaseArtifactManifest.ps1 `
  -ManifestPath android-release-assets/release-artifacts-v2.json `
  -ArtifactDirectory android-release-assets

$checksumTargets = @(
  $apkPaths[0].FullName,
  $metadataPath,
  (Join-Path $PWD 'android-release-assets/release-artifacts-v2.json'))
$checksumLines = foreach ($path in $checksumTargets) {
  $file = Get-Item -LiteralPath $path
  $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
  "$hash  $($file.Name)"
}
[IO.File]::WriteAllText(
  (Join-Path $PWD 'android-release-assets/SHA256SUMS'),
  (($checksumLines -join "`n") + "`n"),
  [Text.UTF8Encoding]::new($false))
