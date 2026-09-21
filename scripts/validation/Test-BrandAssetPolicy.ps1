<#
.SYNOPSIS
Fails closed when shipped desktop assets or brand-icon safeguards diverge from policy.

.DESCRIPTION
OTP Harbor permits local, user-initiated icon-pack imports but does not distribute
third-party service logos. This validation keeps packaged application assets on
explicit allowlists, verifies every tracked visual-media file against the reviewed
provenance ledger, rejects tracked archives, and protects the required policy, UI
disclosure, notice preservation, and offline-only import boundary.
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
$assetProvenance = Read-RepositoryFile 'docs/assets/ASSET_PROVENANCE.md'
$desktopStringsPath = Join-Path $repositoryRoot 'TOTP.UI.Avalonia.Desktop/Localization/Strings.resx'
$brandService = Read-RepositoryFile 'TOTP.Infrastructure/Services/SimpleIconsBrandIconPackService.cs'
$resolverDatabaseText = Read-RepositoryFile 'TOTP.Infrastructure/Branding/issuer-resolver.v1.json'

foreach ($requiredText in @(
    'intentionally does not distribute third-party brand assets',
    'does not bundle, host, provide, or automatically download third-party brand logos or icon packs',
    'does not grant any license or other rights to third-party trademarks',
    'used solely for identification and interoperability purposes',
    'do not imply sponsorship, endorsement, certification, authorization, or affiliation',
    'does not upload issuer names, account names, imported icons, or other icon-matching data',
    'does not publish, mirror, endorse, or designate an official mixed-logo pack',
    'versioned, logo-free resolver database',
    'BrandIcons/account-brand-settings.json',
    'contains no issuer, account name, OTP secret, icon path, or artwork'
)) {
    if (-not $policy.Contains($requiredText, [StringComparison]::Ordinal)) {
        throw "The brand-asset policy is missing the required safeguard: $requiredText"
    }
}

if ($resolverDatabaseText -match '(?i)https?://|\.svg\b|\.png\b|"(?:artwork|assetUrl|downloadUrl|iconFile)"') {
    throw 'The built-in issuer resolver must contain aliases only, never artwork or asset locations.'
}
$resolverDatabase = $resolverDatabaseText | ConvertFrom-Json
if ($resolverDatabase.schemaVersion -ne 1) {
    throw 'The built-in issuer resolver has an unsupported schema version.'
}
$resolverEntries = @($resolverDatabase.entries)
if ($resolverEntries.Count -eq 0 -or $resolverEntries.Count -gt 512) {
    throw 'The built-in issuer resolver entry count is outside the reviewed bounds.'
}
$resolverBrandIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($entry in $resolverEntries) {
    $brandId = [string]$entry.brandId
    $aliases = @($entry.aliases)
    if ($brandId -cnotmatch '^[a-z0-9_]+$' -or -not $resolverBrandIds.Add($brandId)) {
        throw "The built-in issuer resolver contains an invalid or duplicate brand id: $brandId"
    }
    if ($aliases.Count -eq 0 -or $aliases.Count -gt 32) {
        throw "The built-in issuer resolver has an invalid alias count for: $brandId"
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

$brandFormatHelp = @($desktopStrings.root.data | Where-Object name -eq 'BrandIconsFormatHelp')
if ($brandFormatHelp.Count -ne 1) {
    throw 'The invariant BrandIconsFormatHelp disclosure is missing or duplicated.'
}
foreach ($requiredText in @('canonical IDs', 'Nested folders', 'duplicate', 'rejected')) {
    if (-not ([string]$brandFormatHelp[0].value).Contains($requiredText, [StringComparison]::Ordinal)) {
        throw "The filename-indexed icon-pack guidance is missing: $requiredText"
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

$approvedAndroidDrawables = @('splash_screen.xml')
$androidDrawablesRoot = Join-Path $repositoryRoot 'TOTP.UI.Avalonia.Android/Resources/drawable'
$actualAndroidDrawables = @(Get-ChildItem -LiteralPath $androidDrawablesRoot -Recurse -File |
    ForEach-Object {
        [IO.Path]::GetRelativePath($androidDrawablesRoot, $_.FullName).Replace(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar)
    } |
    Sort-Object)
$unexpectedAndroidDrawables = @($actualAndroidDrawables | Where-Object { $_ -notin $approvedAndroidDrawables })
$missingAndroidDrawables = @($approvedAndroidDrawables | Where-Object { $_ -notin $actualAndroidDrawables })
if ($unexpectedAndroidDrawables.Count -gt 0 -or $missingAndroidDrawables.Count -gt 0) {
    throw "Shipped Android drawables differ from the reviewed allowlist. Unexpected: $($unexpectedAndroidDrawables -join ', '); missing: $($missingAndroidDrawables -join ', ')"
}

$trackedFiles = @(& git -C $repositoryRoot ls-files)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to enumerate tracked repository files for brand-asset validation.'
}

$trackedArchives = @($trackedFiles | Where-Object { $_ -match '\.(?:7z|rar|tar|tar\.gz|tgz|zip)$' })
if ($trackedArchives.Count -gt 0) {
    throw "Tracked archives require removal or an explicit policy exception: $($trackedArchives -join ', ')"
}

$reviewedAssets = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
$reviewedAssetPattern = '(?m)^\| `(?<path>[^`]+)` \| `(?<hash>[0-9a-f]{64})` \|\r?$'
foreach ($match in [Text.RegularExpressions.Regex]::Matches($assetProvenance, $reviewedAssetPattern)) {
    $relativePath = $match.Groups['path'].Value
    if (-not $reviewedAssets.TryAdd($relativePath, $match.Groups['hash'].Value)) {
        throw "The asset provenance ledger contains a duplicate path: $relativePath"
    }
}

$trackedVisualAssets = @($trackedFiles | Where-Object {
    $_ -match '\.(?:avif|bmp|gif|heic|ico|jpe?g|mkv|mov|mp4|pdf|png|svg|webm|webp)$'
})
foreach ($relativePath in $trackedVisualAssets) {
    if (-not $reviewedAssets.ContainsKey($relativePath)) {
        throw "Tracked visual media lacks a reviewed provenance hash: $relativePath"
    }

    $assetPath = Join-Path $repositoryRoot $relativePath
    $actualHash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -cne $reviewedAssets[$relativePath]) {
        throw "Tracked visual media changed without provenance review: $relativePath"
    }
}

foreach ($requiredText in @(
    'CopyNoticeIfPresentAsync(archive, archivePrefix, "LICENSE.md"',
    'CopyNoticeIfPresentAsync(archive, archivePrefix, "DISCLAIMER.md"',
    'CopyGenericNoticesAsync(archive, stagingDirectory'
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
foreach ($requiredText in @(
    'AccountBrandSettingsFileName = "account-brand-settings.json"',
    'SetAccountBrandIdAsync',
    'new AccountBrandOverride(pair.Key, pair.Value)'
)) {
    if (-not $brandService.Contains($requiredText, [StringComparison]::Ordinal)) {
        throw "The display-only account icon mapping boundary is missing: $requiredText"
    }
}

Write-Output "Third-party brand assets remain user-supplied and local-only; $($trackedVisualAssets.Count) tracked visual-media files match the reviewed provenance ledger."
