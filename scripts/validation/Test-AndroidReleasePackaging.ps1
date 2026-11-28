<#
.SYNOPSIS
Regression-checks the permanent Android identity, version mapping, and release controls.
#>
$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))

function Read-RequiredFile([string]$RelativePath) {
    $path = Join-Path $repositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required Android release file is missing: $RelativePath"
    }
    [IO.File]::ReadAllText($path)
}

$project = Read-RequiredFile "OtpHarbor.UI.Avalonia.Android\OtpHarbor.UI.Avalonia.Android.csproj"
$manifest = Read-RequiredFile "OtpHarbor.UI.Avalonia.Android\Properties\AndroidManifest.xml"
$installer = Read-RequiredFile "scripts/testing/android\Install-AndroidDevelopmentBuild.ps1"
$workflow = Read-RequiredFile ".github\workflows\build-and-test.yml"
$workflow += [IO.File]::ReadAllText((Join-Path $repositoryRoot '.github/workflows/reusable-desktop-package.yml'))
$workflow += [IO.File]::ReadAllText((Join-Path $repositoryRoot '.github/workflows/reusable-validation.yml'))
$workflow += [IO.File]::ReadAllText((Join-Path $repositoryRoot '.github/actions/validate-application/action.yml'))
$workflow += [IO.File]::ReadAllText((Join-Path $repositoryRoot '.github/workflows/reusable-android-package.yml'))
$workflow += [IO.File]::ReadAllText((Join-Path $repositoryRoot 'scripts/release/shared/Prepare-DesktopReleaseMetadata.ps1'))
$workflow += [IO.File]::ReadAllText((Join-Path $repositoryRoot 'scripts/release/shared/Prepare-AndroidReleaseMetadata.ps1'))
$documentation = Read-RequiredFile "docs\release\ANDROID.md"
$releasePackager = Read-RequiredFile "scripts/release/android/New-AndroidRelease.ps1"
$nativeLock = Read-RequiredFile 'OtpHarbor.UI.Avalonia.Android/packages.nativeaot.lock.json' | ConvertFrom-Json
$nativeCompiler = $nativeLock.dependencies.'net10.0-android36.0'.'Microsoft.DotNet.ILCompiler'
if ($nativeCompiler.type -ne 'Direct' -or $nativeCompiler.resolved -ne '10.0.15' -or [string]::IsNullOrWhiteSpace($nativeCompiler.contentHash)) {
    throw 'The NativeAOT compiler must be locked with its version and package content hash.'
}
foreach ($nativeControl in @('RestorePackagesWithLockFile', 'packages.nativeaot.lock.json', 'SkipUniversalNativeBinaryCopy')) {
    if (-not $project.Contains($nativeControl, [StringComparison]::Ordinal)) { throw "Missing NativeAOT build control: $nativeControl" }
}
foreach ($nativeControl in @('-UseNativeAot', 'Install-AndroidNativeAotToolchain.ps1', 'NativeAotCompatibilitySmoke', 'Test-AndroidRuntimePayload.ps1')) {
    if (-not $workflow.Contains($nativeControl, [StringComparison]::Ordinal)) { throw "Missing NativeAOT CI control: $nativeControl" }
}
if (-not $releasePackager.Contains('Test-AndroidRuntimePayload.ps1', [StringComparison]::Ordinal)) { throw 'Release APK runtime payloads must be validated before publication.' }
$sdkPolicy = (Read-RequiredFile 'global.json' | ConvertFrom-Json).sdk
if ($sdkPolicy.version -ne '10.0.404') {
    throw 'The Android release must use the validated .NET SDK.'
}
if ($null -ne $sdkPolicy.PSObject.Properties['workloadVersion']) {
    throw 'Pin the workload set in Android installation steps; global.json must also support SDK-only security and desktop jobs.'
}
if ([regex]::Matches($workflow, '(?m)^\s*run: dotnet workload install android --version 10\.0\.403\s*$').Count -ne 2) {
    throw 'Both Android build and production release jobs must explicitly install workload set 10.0.403.'
}
foreach ($buildHelper in @($releasePackager, (Read-RequiredFile 'scripts/testing/android/Build-AndroidAotExperiment.ps1'))) {
    if (-not $buildHelper.Contains('scripts/validation/Test-AndroidToolchain.ps1', [StringComparison]::Ordinal)) {
        throw 'Local Android release and runtime comparison builds must validate the pinned toolchain.'
    }
}
if ($workflow.Contains('dotnet workload install android --skip-manifest-update', [StringComparison]::Ordinal)) {
    throw 'Android CI must install the pinned workload set instead of reusing runner manifests.'
}
if (-not $project.Contains('<RuntimeIdentifiers Condition="''$(RuntimeIdentifier)'' == ''''">android-arm64;android-x64</RuntimeIdentifiers>', [StringComparison]::Ordinal)) {
    throw 'The Android project must declare both universal APK runtime identifiers.'
}
foreach ($runtimeControl in @('-p:RuntimeIdentifier=', '-p:UseCurrentRuntimeIdentifier=false')) {
    if (-not $releasePackager.Contains($runtimeControl, [StringComparison]::Ordinal)) {
        throw "The Android release is missing an explicit runtime control: $runtimeControl"
    }
}
$fingerprintParserPath = Join-Path $repositoryRoot `
    "scripts/release/android/Get-AndroidSigningCertificateFingerprint.ps1"

foreach ($required in @(
    '<AndroidProductionApplicationId>io.github.legends.otpharbor</AndroidProductionApplicationId>',
    '<ApplicationId Condition="''$(Configuration)'' == ''Debug''">$(AndroidProductionApplicationId).debug</ApplicationId>',
    '<AndroidPackageFormats Condition="''$(Configuration)'' == ''Release''">apk</AndroidPackageFormats>',
    "'`$(Configuration)' == 'Debug' and '`$(EnableMarketingCapture)' == ''",
    '<EnableMarketingCapture>false</EnableMarketingCapture>',
    "'`$(Configuration)' == 'Debug' and '`$(EnableMarketingCapture)' == 'true'",
    "'`$(Configuration)' == 'Release' and '`$(EnableMarketingCapture)' == 'true'",
    'EnableMarketingCapture is restricted to the isolated Debug package.')) {
    if (-not $project.Contains($required, [StringComparison]::Ordinal)) {
        throw "The Android project is missing a permanent identity or distribution control: $required"
    }
}
$activity = Read-RequiredFile "OtpHarbor.UI.Avalonia.Android\MainActivity.cs"
$mobileMainView = Read-RequiredFile "OtpHarbor.UI.Avalonia.Mobile\Views\MainView.axaml.cs"
if (-not $activity.Contains('#if OTP_HARBOR_MARKETING_CAPTURE', [StringComparison]::Ordinal)) {
    throw 'The isolated Debug capture mode is not compile-time restricted.'
}
foreach ($requiredBackNavigationControl in @(
    'BackRequested += MainViewBackRequested',
    'BackRequested -= MainViewBackRequested',
    'CanHandleSystemBack',
    'e.Handled = true',
    'TryHandleBackNavigationAsync()',
    '_backNavigationInProgress')) {
    if (-not $mobileMainView.Contains($requiredBackNavigationControl, [StringComparison]::Ordinal)) {
        throw "Android system-back navigation is missing: $requiredBackNavigationControl"
    }
}
if (-not $manifest.Contains('android:enableOnBackInvokedCallback="true"', [StringComparison]::Ordinal)) {
    throw 'Android predictive-back integration is not enabled in the manifest.'
}
foreach ($obsoleteBackNavigationControl in @(
    'override void OnBackPressed()',
    'RegisterOnBackInvokedCallback(')) {
    if ($activity.Contains($obsoleteBackNavigationControl, [StringComparison]::Ordinal)) {
        throw "MainActivity bypasses Avalonia system-back routing: $obsoleteBackNavigationControl"
    }
}
if (-not $manifest.Contains('android:authorities="${applicationId}.fileprovider"', [StringComparison]::Ordinal) -or
    -not $installer.Contains("`$packageName = 'io.github.legends.otpharbor.debug'", [StringComparison]::Ordinal)) {
    throw "The Android manifest or development installer can collide with the production package."
}
foreach ($requiredInstallerControl in @(
    '[int]$Port',
    '[switch]$ForceBuild',
    '[switch]$CleanBuild',
    '[switch]$FastDeploy',
    'Get-AndroidBuildInputFingerprint',
    'Get-AndroidRuntimeIdentifier.ps1',
    'ro.product.cpu.abi',
    'android-development-build-state.json',
    "`$cachedBuildMode -ceq 'IncrementalDeviceApk'",
    '$cachedRuntimeIdentifier -ceq $runtimeIdentifier',
    '$cachedApkHash -ceq $existingApkHash',
    'Android build inputs are unchanged; skipping the build.',
    '--target:Install',
    '-p:EmbedAssembliesIntoApk=false',
    'NuGetLockFilePath',
    '--runtime',
    '--no-incremental',
    '--target:SignAndroidPackage',
    '& $adbPath connect $TargetDevice',
    'Test-AndroidTcpEndpoint $TargetDevice',
    '_adb-tls-pairing\._tcp',
    'Enter the six-digit code when ADB prompts for the pairing code.',
    '& $adbPath pair $pairingEndpoint',
    'Pairing succeeded, but Android device',
    '& $adbPath -s $device install --no-incremental -r $apkPath',
    'shell am force-stop $packageName')) {
    if (-not $installer.Contains($requiredInstallerControl, [StringComparison]::Ordinal)) {
        throw "The Android development installer is missing a build-reuse or targeted-device control: $requiredInstallerControl"
    }
}

$runtimeIdentifierResolver = Join-Path $repositoryRoot `
    'scripts/testing/android\Get-AndroidRuntimeIdentifier.ps1'
foreach ($case in @(
    @{ Abi = 'arm64-v8a'; RuntimeIdentifier = 'android-arm64' },
    @{ Abi = 'x86_64'; RuntimeIdentifier = 'android-x64' })) {
    $actualRuntimeIdentifier = & $runtimeIdentifierResolver -Abi $case.Abi
    if ($actualRuntimeIdentifier -cne $case.RuntimeIdentifier) {
        throw "Android ABI $($case.Abi) mapped to an unexpected runtime identifier."
    }
}
try {
    & $runtimeIdentifierResolver -Abi 'mips64' 2>$null | Out-Null
    throw 'The Android ABI resolver accepted an unsupported ABI.'
}
catch {
    if ($_.Exception.Message -eq 'The Android ABI resolver accepted an unsupported ABI.') { throw }
}

$versionCases = @(
    @{ Tag = "android-v2.0.0"; Code = 20000099L; Name = "2.0.0" },
    @{ Tag = "android-v2.0.17"; Code = 20001799L; Name = "2.0.17" },
    @{ Tag = "android-v2.1.0"; Code = 20100099L; Name = "2.1.0" })
foreach ($case in $versionCases) {
    $actual = & (Join-Path $repositoryRoot "scripts/release/android/Get-AndroidReleaseVersion.ps1") `
        -ReleaseTag $case.Tag | ConvertFrom-Json
    if ($actual.applicationId -cne "io.github.legends.otpharbor" -or
        [int64]$actual.versionCode -ne $case.Code -or
        $actual.displayVersion -cne $case.Name) {
        throw "Android version mapping failed for $($case.Tag)."
    }
}

foreach ($invalidTag in @(
    "2.0.0",
    "v2.0.0",
    "android-v2.0",
    "android-v2.0.0-rc1",
    "android-v210.0.0")) {
    try {
        & (Join-Path $repositoryRoot "scripts/release/android/Get-AndroidReleaseVersion.ps1") `
            -ReleaseTag $invalidTag 2>$null | Out-Null
        throw "Android version mapping accepted invalid tag $invalidTag."
    }
    catch {
        if ($_.Exception.Message -eq "Android version mapping accepted invalid tag $invalidTag.") { throw }
    }
}

$rsa = [Security.Cryptography.RSA]::Create(2048)
try {
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
        'CN=OTP Harbor Android release parser test',
        $rsa,
        [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $testCertificate = $request.CreateSelfSigned(
        [DateTimeOffset]::UtcNow.AddMinutes(-1),
        [DateTimeOffset]::UtcNow.AddMinutes(5))
    try {
        $expectedFingerprint = $testCertificate.GetCertHashString(
            [Security.Cryptography.HashAlgorithmName]::SHA256).ToUpperInvariant()
        $certificatePem = $testCertificate.ExportCertificatePem()
    }
    finally {
        $testCertificate.Dispose()
    }
}
finally {
    $rsa.Dispose()
}

$verificationOutput = @('Verifies', $certificatePem)
$actualFingerprint = & $fingerprintParserPath -VerificationOutput $verificationOutput
if ($actualFingerprint -cne $expectedFingerprint) {
    throw 'The Android signing-certificate fingerprint parser changed the certificate digest.'
}
try {
    & $fingerprintParserPath -VerificationOutput @(
        $certificatePem,
        $certificatePem) |
        Out-Null
    throw 'The Android signing-certificate fingerprint parser accepted multiple signers.'
}
catch {
    if ($_.Exception.Message -eq
        'The Android signing-certificate fingerprint parser accepted multiple signers.') { throw }
}
try {
    & $fingerprintParserPath -VerificationOutput @(
        'Verifies',
        'Signer #1 certificate SHA-256 digest: FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF') |
        Out-Null
    throw 'The Android signing-certificate fingerprint parser accepted digest text without a certificate.'
}
catch {
    if ($_.Exception.Message -eq
        'The Android signing-certificate fingerprint parser accepted digest text without a certificate.') { throw }
}
if (-not $releasePackager.Contains('verify --verbose --print-certs-pem', [StringComparison]::Ordinal) -or
    -not $releasePackager.Contains('2>&1', [StringComparison]::Ordinal) -or
    -not $releasePackager.Contains('Get-AndroidSigningCertificateFingerprint.ps1', [StringComparison]::Ordinal)) {
    throw 'The Android release packager does not verify and parse the apksigner PEM certificate.'
}

foreach ($required in @(
    "android-build-test:",
    "package-android-release:",
    "publish-android-release:",
    "- 'android-v*'",
    "startsWith(github.ref, 'refs/tags/android-v')",
    "java-version: '21'",
    "environment: android-release",
    "ANDROID_SIGNING_KEYSTORE_BASE64",
    "ANDROID_SIGNING_CERTIFICATE_SHA256",
    'name: android-release-${{ github.sha }}',
    'name: OTP Harbor Android ${{ steps.versioning.outputs.release_version }}',
    'make_latest: false',
    "android-release.json",
    "New-AndroidRelease.ps1")) {
    if (-not $workflow.Contains($required, [StringComparison]::Ordinal)) {
        throw "The release workflow is missing an Android control: $required"
    }
}
foreach ($required in @(
    "io.github.legends.otpharbor",
    "Android developer verification",
    "same app-signing key",
    "android-v<major>.<minor>.<patch>")) {
    if (-not $documentation.Contains($required, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The Android release guide is missing required guidance: $required"
    }
}

$desktopProbe = [regex]::Match($workflow, '(?ms)^  m3-package-probe:\r?\n(?<body>.*?)(?=^  [a-z][a-z0-9-]*:|\z)').Groups['body'].Value
if (-not $desktopProbe.Contains("if: `${{ !startsWith(github.ref, 'refs/tags/android-v') }}", [StringComparison]::Ordinal)) {
    throw 'Android release tags must skip desktop packaging probes.'
}
foreach ($job in @('package-android-release', 'publish-android-release')) {
    $jobBody = [regex]::Match($workflow, "(?ms)^  ${job}:\r?\n(?<body>.*?)(?=^  [a-z][a-z0-9-]*:|\z)").Groups['body'].Value
    $dependencies = [regex]::Match($jobBody, '(?m)^    needs:\s*(?<value>.+)$').Groups['value'].Value
    if ([string]::IsNullOrWhiteSpace($dependencies) -or $dependencies.Contains('m3-package-probe', [StringComparison]::Ordinal)) {
        throw 'Android publication must have independent Android validation and packaging dependencies.'
    }
}

$desktopPublicationIndex = $workflow.IndexOf('  publish-avalonia-release:', [StringComparison]::Ordinal)
$androidPublicationIndex = $workflow.IndexOf('  publish-android-release:', [StringComparison]::Ordinal)
if ($desktopPublicationIndex -lt 0 -or $androidPublicationIndex -le $desktopPublicationIndex) {
    throw 'Independent desktop and Android publication jobs are required.'
}
$desktopPublication = $workflow.Substring(
    $desktopPublicationIndex,
    $androidPublicationIndex - $desktopPublicationIndex)
if ($desktopPublication.Contains('OTP-Harbor-android-universal-', [StringComparison]::Ordinal) -or
    $desktopPublication.Contains('android-release.json', [StringComparison]::Ordinal)) {
    throw 'The desktop release still includes Android release assets.'
}

Write-Output "Android identity, versioning, and release packaging controls are valid."
