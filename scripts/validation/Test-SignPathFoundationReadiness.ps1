Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "../..")).Path

function Read-RepositoryFile {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $path = Join-Path $repositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required SignPath readiness file is missing: $RelativePath"
    }

    return [IO.File]::ReadAllText($path)
}

$readme = Read-RepositoryFile "readme.md"
$policy = Read-RepositoryFile "CODE_SIGNING_POLICY.md"
$privacy = Read-RepositoryFile "PRIVACY.md"
$workflow = Read-RepositoryFile ".github/workflows/build-and-test.yml"
$signedPayloadValidation = Read-RepositoryFile "scripts/release/Apply-SignPathSignedPayload.ps1"
$codeOwners = Read-RepositoryFile ".github/CODEOWNERS"
$buildMetadata = Read-RepositoryFile "Directory.Build.props"
$desktopProject = Read-RepositoryFile "TOTP.UI.Avalonia.Desktop/TOTP.UI.Avalonia.Desktop.csproj"
$assetProvenance = Read-RepositoryFile "docs/assets/ASSET_PROVENANCE.md"

$requiredReadmeText = @(
    "## Code signing policy",
    "The previous SignPath Foundation application was not approved at this stage.",
    "Current GitHub preview builds are unsigned.",
    "[OTP Harbor privacy policy](PRIVACY.md)"
)
foreach ($requiredText in $requiredReadmeText) {
    if (-not $readme.Contains($requiredText, [StringComparison]::Ordinal)) {
        throw "The README is missing required code-signing text: $requiredText"
    }
}

foreach ($requiredText in @(
    "The previous SignPath Foundation application was not approved at this stage.",
    "Current GitHub preview builds are unsigned.",
    "## Team roles",
    "## Build and signing controls",
    "## Privacy"
)) {
    if (-not $policy.Contains($requiredText, [StringComparison]::Ordinal)) {
        throw "The code signing policy is missing: $requiredText"
    }
}
if (-not $privacy.Contains("does not contain telemetry", [StringComparison]::Ordinal)) {
    throw "The privacy policy does not state the telemetry posture."
}
if (-not $codeOwners.Contains("/.github/workflows/ @Legends", [StringComparison]::Ordinal)) {
    throw "Release workflows are not covered by CODEOWNERS."
}
if (-not $buildMetadata.Contains("<Product>OTP Harbor</Product>", [StringComparison]::Ordinal)) {
    throw "First-party PE product metadata is not centrally defined."
}
if (($desktopProject.Split('IncludeSourceRevisionInInformationalVersion=$(IncludeSourceRevisionInInformationalVersion)').Count - 1) -lt 2 -or
    ($desktopProject.Split('RemoveProperties="RuntimeIdentifier;SelfContained;PublishSingleFile;PublishTrimmed"').Count - 1) -lt 2) {
    throw "The updater build does not consistently inherit release metadata independently of the desktop runtime identifier."
}

$reviewedAssets = [ordered]@{
    "TOTP.UI.Avalonia.Desktop/Assets/Icons/app-1024.png" = "66748954507b3f9f9cff87dc23c97134c1d7d029e8275de179b9f3872f2d12b4"
    "TOTP.UI.Avalonia.Desktop/Assets/Icons/app-128.png"  = "26fe7fe9a91c7f2e939c7d794cbade4d1e22090ef3c40a59b8ae9ffb3c9aaf88"
    "TOTP.UI.Avalonia.Desktop/Assets/Icons/app.ico"      = "7a71a423982499c438177e3b58126f003c3ece9a66cb2b91c07dc50a812ab81e"
    "TOTP.UI.Avalonia.Desktop/Assets/flags/en.png"       = "1c2bcc20e5985e5f03a3a440f198b5d08a4ac609e9cebba00b639b0e50fba8fc"
    "TOTP.UI.Avalonia.Desktop/Assets/flags/de.png"       = "2c8f253f3401d18df0a47bd7906102cf78ea7e4a2caac9e4c6f4efebc906de0a"
    "TOTP.UI.Avalonia.Desktop/Assets/flags/fr.png"       = "b962887c6a6317b8e60a1c0b33ae5fed16b453a83e9026043480ccfd3cc3a340"
    "TOTP.UI.Avalonia.Desktop/Assets/flags/es.png"       = "d45958f491e9cf1d10c0e6de74970c5fed11e826e702c14c9009197842e6d1bd"
    "docs/images/readme/app.png"                         = "3730253e1d8579bae7fb49d173ad83b91ad7a2f90f5f5a759a29f048abe1e657"
    "docs/images/social/otp-harbor-social-preview.jpg"   = "2ca1ebc4d4dabbb5f8061013432c4d3dc5708efa03ab746b20e4e83686de6725"
}
foreach ($entry in $reviewedAssets.GetEnumerator()) {
    $assetPath = Join-Path $repositoryRoot $entry.Key
    if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
        throw "Reviewed project asset is missing: $($entry.Key)"
    }
    $actualHash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -cne $entry.Value) {
        throw "Project asset changed without a matching provenance review: $($entry.Key)"
    }
    if (-not $assetProvenance.Contains($entry.Value, [StringComparison]::Ordinal)) {
        throw "The asset provenance record is missing the reviewed hash for $($entry.Key)."
    }
}

$requiredWorkflowText = @(
    "signpath/github-action-submit-signing-request@c92b958760219087e01f8d67a1669ed57afe2627",
    "signing-policy-slug: release-signing",
    "artifact-configuration-slug: windows-release-v1",
    "SignPath Windows rehearsal (no release)"
)
foreach ($requiredText in $requiredWorkflowText) {
    if (-not $workflow.Contains($requiredText, [StringComparison]::Ordinal)) {
        throw "The release workflow is missing the SignPath control: $requiredText"
    }
}
if (-not $signedPayloadValidation.Contains("Get-AuthenticodeSignature", [StringComparison]::Ordinal)) {
    throw "The signed-payload validation script does not verify Authenticode signatures."
}
if ($workflow -match 'SIGNING_CERT_(BASE64|PASSWORD)') {
    throw "The hosted release workflow must not accept exportable Windows certificate material."
}

Write-Output "Code-signing disclosure and dormant SignPath fail-closed controls are present."
