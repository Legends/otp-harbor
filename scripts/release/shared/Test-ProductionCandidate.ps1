#requires -Version 7.0
param([Parameter(Mandatory)][ValidateSet('desktop', 'android')][string]$Family)
$ErrorActionPreference = 'Stop'
$candidate = Get-Content candidate.json -Raw | ConvertFrom-Json
$plan = $candidate.plan
$directory = "candidate-payload/$Family"
& (Join-Path $PSScriptRoot 'Test-ReleaseArtifactManifest.ps1') -ManifestPath "$directory/release-artifacts-v2.json" -ArtifactDirectory $directory
$manifest = Get-Content "$directory/release-artifacts-v2.json" -Raw | ConvertFrom-Json
$number = if ($Family -eq 'desktop') { $plan.desktopVersion } else { $plan.androidVersion }
if ($manifest.sourceCommit -cne $plan.sourceCommit -or $manifest.releaseVersion -cne $number) { throw 'Release manifest identity mismatch.' }
if ($Family -eq 'desktop') {
    $keyDirectory = Join-Path $env:RUNNER_TEMP 'otp-release-verification-key'
    New-Item -ItemType Directory -Path $keyDirectory -Force | Out-Null
    $settings = Get-Content (Join-Path $PSScriptRoot '../../../OtpHarbor.UI.Avalonia.Desktop/appsettings.json') -Raw | ConvertFrom-Json
    [IO.File]::WriteAllText((Join-Path $keyDirectory 'NetSparkle_Ed25519.pub'), $settings.AutoUpdate.PublicKey.Trim(), [Text.Encoding]::ASCII)
    foreach ($name in @('release-artifacts-v2.json', 'appcast-v2.xml')) {
        $signature = (Get-Content "$directory/$name.signature" -Raw).Trim()
        & netsparkle-generate-appcast --verify "$directory/$name" --signature $signature --key-path $keyDirectory
        if ($LASTEXITCODE -ne 0) { throw 'Production update metadata signature verification failed.' }
    }
    & (Join-Path $PSScriptRoot 'Test-AvaloniaAppcast.ps1') -AppcastPath "$directory/appcast-v2.xml" `
        -ManifestPath "$directory/release-artifacts-v2.json" -BaseDownloadUrl "https://github.com/$env:GITHUB_REPOSITORY/releases/download/v$number/"
}
else {
    $metadata = Get-Content "$directory/android-release.json" -Raw | ConvertFrom-Json
    $android = & (Join-Path $PSScriptRoot '../android/Get-AndroidReleaseVersion.ps1') -ReleaseTag "android-v$number" | ConvertFrom-Json
    $package = Join-Path $directory $metadata.fileName
    $tools = Get-ChildItem (Join-Path $env:ANDROID_HOME 'build-tools') -Directory |
        Where-Object { (Test-Path (Join-Path $_.FullName 'apksigner.bat')) -and (Test-Path (Join-Path $_.FullName 'aapt.exe')) } |
        Sort-Object Name -Descending | Select-Object -First 1
    if ($null -eq $tools) { throw 'Official Android signature/manifest verification tools are missing.' }
    $signature = & (Join-Path $tools.FullName 'apksigner.bat') verify --verbose --print-certs-pem $package 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'APK signature verification failed.' }
    $fingerprint = & (Join-Path $PSScriptRoot '../android/Get-AndroidSigningCertificateFingerprint.ps1') -VerificationOutput $signature
    if ($fingerprint -ine $metadata.certificateSha256) { throw 'APK signing identity differs from the verified candidate.' }
    $badging = & (Join-Path $tools.FullName 'aapt.exe') dump badging $package
    if ($LASTEXITCODE -ne 0 -or ($badging -match '^application-debuggable')) { throw 'Invalid or debuggable production APK.' }
    $identity = @($badging | Where-Object { $_ -match '^package:\s' })
    if ($identity.Count -ne 1 -or $identity[0] -notmatch "name='$([regex]::Escape($android.applicationId))'" -or
        $identity[0] -notmatch "versionCode='$($android.versionCode)'" -or $identity[0] -notmatch "versionName='$([regex]::Escape($number))'") {
        throw 'APK manifest identity/version does not match the candidate.'
    }
    & (Join-Path $PSScriptRoot '../android/Test-AndroidRuntimePayload.ps1') -ApkPath $package -Runtime NativeAOT
}
