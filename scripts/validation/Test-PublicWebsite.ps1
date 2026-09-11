<#
.SYNOPSIS
Validates the public website, SEO metadata, and deployment assets.

.DESCRIPTION
Checks the canonical page, verification tags, structured data, social metadata, release messaging, robots and sitemap files, required screenshots/icons, and social-preview size so incomplete website changes fail CI.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path

function Read-RequiredFile {
    param([Parameter(Mandatory)][string]$RelativePath)

    $path = Join-Path $repositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required website file is missing: $RelativePath"
    }
    return [IO.File]::ReadAllText($path)
}

$index = Read-RequiredFile 'site/index.html'
$compactIndex = [Text.RegularExpressions.Regex]::Replace($index, '\s+', ' ')
$styles = Read-RequiredFile 'site/styles.css'
$robots = Read-RequiredFile 'site/robots.txt'
$sitemap = Read-RequiredFile 'site/sitemap.xml'
$pagesWorkflow = Read-RequiredFile '.github/workflows/pages.yml'
$indexNowKey = '1b6f7ab9795743588fa8e24157ad1541'
$indexNowKeyFile = Read-RequiredFile "site/$indexNowKey.txt"
Read-RequiredFile 'docs/images/readme/app.png' | Out-Null
Read-RequiredFile 'TOTP.UI.Avalonia.Desktop/Assets/Icons/app-1024.png' | Out-Null

$socialPreviewPath = Join-Path $repositoryRoot 'docs/images/social/otp-harbor-social-preview.jpg'
if (-not (Test-Path -LiteralPath $socialPreviewPath -PathType Leaf)) {
    throw 'The optimized social-preview image is missing.'
}
if ((Get-Item -LiteralPath $socialPreviewPath).Length -ge 1MB) {
    throw 'The social-preview image must remain smaller than 1 MB for GitHub upload.'
}

foreach ($requiredText in @(
    '<title>OTP Harbor — Open-Source TOTP & 2FA Authenticator</title>',
    '<meta name="google-site-verification" content="I36j8PWZYmhKsRKKNVM-fmcGW7wXbJ10fmbOe_4Az0U">',
    '<meta name="msvalidate.01" content="EAC868BC10B59CB9E6BFF0CE79DEEBAC">',
    '<link rel="canonical" href="https://legends.github.io/otp-harbor/">',
    '<meta property="og:image" content="https://legends.github.io/otp-harbor/assets/social-preview.jpg">',
    'type="application/ld+json"',
    '"@type": "SoftwareApplication"',
    '"@type": "Offer"',
    'https://apps.microsoft.com/detail/9P31KH5L924P',
    'Get it from Microsoft Store',
    'OTP Harbor is now publicly available from Microsoft Store, the primary Windows channel.',
    'GitHub previews are clearly labeled and use a signed application update feed.',
    'Can you test OTP Harbor on a MacBook?',
    'Test only with synthetic accounts',
    'Right-click an existing desktop account to edit it, show its QR code or delete it after confirmation.',
    'Settings &gt; Import / Export'
)) {
    if (-not $compactIndex.Contains($requiredText, [StringComparison]::Ordinal)) {
        throw "The public website is missing required content: $requiredText"
    }
}

$mainScreenshotPath = Join-Path $repositoryRoot 'docs/images/readme/app.png'
if ((Get-Item -LiteralPath $mainScreenshotPath).Length -ge 1MB) {
    throw 'The main application screenshot must remain smaller than 1 MB.'
}

if ($index -match 'aggregateRating|reviewCount|downloadCount|google-analytics|googletagmanager') {
    throw 'The website must not publish unverified popularity signals or analytics.'
}
if (-not $styles.Contains('prefers-reduced-motion', [StringComparison]::Ordinal)) {
    throw 'The website is missing its reduced-motion accessibility rule.'
}
if (-not $robots.Contains('Sitemap: https://legends.github.io/otp-harbor/sitemap.xml', [StringComparison]::Ordinal) -or
    -not $sitemap.Contains('<loc>https://legends.github.io/otp-harbor/</loc>', [StringComparison]::Ordinal)) {
    throw 'The website crawl metadata does not use the canonical Pages URL.'
}

if ($indexNowKeyFile.Trim() -cne $indexNowKey -or
    -not $pagesWorkflow.Contains("cp site/$indexNowKey.txt _site/", [StringComparison]::Ordinal) -or
    -not $pagesWorkflow.Contains("https://www.bing.com/indexnow", [StringComparison]::Ordinal) -or
    -not $pagesWorkflow.Contains("https://www.googleapis.com/webmasters/v3/sites/", [StringComparison]::Ordinal)) {
    throw 'The website search-engine notification configuration is incomplete.'
}

Write-Output 'Public website content, trust disclosure, and crawl metadata are present.'
