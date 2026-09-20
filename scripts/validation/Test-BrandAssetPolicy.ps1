<#
.SYNOPSIS
Fails closed when shipped desktop assets or brand-icon safeguards diverge from policy.

.DESCRIPTION
OTP Harbor permits local, user-initiated icon-pack imports but does not distribute
third-party service logos. This validation keeps the shipped desktop asset set on
an explicit allowlist and protects the required policy, UI disclosure, notice
preservation, and offline-only import boundary.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path

function Read-RepositoryFile {
    param([Parameter(Mandatory)][string]$RelativePath)

    $path = Join-Path $repositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required brand-asset policy file is missing: $RelativePath"
    }

    return [IO.File]::ReadAllText($path)
}

$policy = Read-RepositoryFile 'docs/assets/BRAND_ICONS.md'
$desktopStringsPath = Join-Path $repositoryRoot 'TOTP.UI.Avalonia.Desktop/Localization/Strings.resx'
$brandService = Read-RepositoryFile 'TOTP.Infrastructure/Services/SimpleIconsBrandIconPackService.cs'

foreach ($requiredText in @(
    'does not publish, mirror, bundle, endorse, or designate an official third-party brand-icon pack',
    'includes no third-party service logos',
    'import only assets they are authorized to use',
    'do not claim ownership, sponsorship, affiliation, certification, or endorsement',
    'prohibits an official mixed-logo pack'
)) {
    if (-not $policy.Contains($requiredText, [StringComparison]::Ordinal)) {
        throw "The brand-asset policy is missing the required safeguard: $requiredText"
    }
}

[xml]$desktopStrings = [IO.File]::ReadAllText($desktopStringsPath)
$brandHelp = @($desktopStrings.root.data | Where-Object name -eq 'BrandIconsHelp')
if ($brandHelp.Count -ne 1) {
    throw 'The invariant BrandIconsHelp disclosure is missing or duplicated.'
}
$brandHelpText = [string]$brandHelp[0].value
foreach ($requiredText in @(
    'includes no third-party logos',
    'not affiliated with or endorsed by',
    'are authorized to use',
    'stays in local app data',
    'no issuer or account information is uploaded'
)) {
    if (-not $brandHelpText.Contains($requiredText, [StringComparison]::Ordinal)) {
        throw "The brand-icon import disclosure is missing: $requiredText"
    }
}

$approvedDesktopAssets = @(
    'Icons/app-1024.png',
    'Icons/app-128.png',
    'Icons/app.ico',
    'flags/de.png',
    'flags/en.png',
    'flags/es.png',
    'flags/fr.png'
)
$desktopAssetsRoot = Join-Path $repositoryRoot 'TOTP.UI.Avalonia.Desktop/Assets'
$actualDesktopAssets = @(Get-ChildItem -LiteralPath $desktopAssetsRoot -Recurse -File |
    ForEach-Object {
        [IO.Path]::GetRelativePath($desktopAssetsRoot, $_.FullName).Replace('\', '/')
    } |
    ForEach-Object {
        $_.Replace([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    } |
    Sort-Object)
$unexpectedAssets = @($actualDesktopAssets | Where-Object { $_ -notin $approvedDesktopAssets })
$missingAssets = @($approvedDesktopAssets | Where-Object { $_ -notin $actualDesktopAssets })
if ($unexpectedAssets.Count -gt 0 -or $missingAssets.Count -gt 0) {
    throw "Shipped desktop assets differ from the reviewed allowlist. Unexpected: $($unexpectedAssets -join ', '); missing: $($missingAssets -join ', ')"
}

foreach ($requiredText in @(
    'CopyNoticeIfPresentAsync(archive, archivePrefix, "LICENSE.md"',
    'CopyNoticeIfPresentAsync(archive, archivePrefix, "DISCLAIMER.md"'
)) {
    if (-not $brandService.Contains($requiredText, [StringComparison]::Ordinal)) {
        throw "The icon importer no longer preserves an upstream notice: $requiredText"
    }
}
$usesHttpClient = $brandService.Contains('HttpClient', [StringComparison]::Ordinal)
$usesWebRequest = $brandService.Contains('WebRequest', [StringComparison]::Ordinal)
if ($usesHttpClient -or $usesWebRequest) {
    throw 'The local icon-pack importer must not download or fetch brand assets.'
}

Write-Output 'Third-party brand assets remain user-supplied, local-only, notice-preserving, and absent from shipped desktop assets.'
