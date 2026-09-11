<#
.SYNOPSIS
Updates or verifies the Microsoft Store version shown in public project metadata.

.DESCRIPTION
Uses a confirmed, publicly deployed four-part Microsoft Store package version as the single input.
The fourth component must be zero. The script updates the canonical version file and every public
location that displays the Store version. Use -Check in CI to detect drift without changing files.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+\.0$')]
    [string]$PackageVersion,

    [string]$RepositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path,

    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$displayVersion = $PackageVersion.Substring(0, $PackageVersion.LastIndexOf('.'))
$versionPath = Join-Path $resolvedRoot 'packaging/windows-store/public-version.json'

if (Test-Path -LiteralPath $versionPath -PathType Leaf) {
    $current = [IO.File]::ReadAllText($versionPath) | ConvertFrom-Json
    if ([version]$PackageVersion -lt [version]$current.packageVersion) {
        throw "Refusing to move the public Store version backwards from $($current.packageVersion) to $PackageVersion."
    }
}

$updates = @(
    @{
        Path = 'readme.md'
        Pattern = '(Microsoft%20Store-)\d+\.\d+\.\d+(-0078D4\?logo=microsoft)'
        Replacement = "`${1}$displayVersion`${2}"
    },
    @{
        Path = 'readme.md'
        Pattern = 'OTP Harbor `\d+\.\d+\.\d+` is publicly available'
        Replacement = "OTP Harbor ``$displayVersion`` is publicly available"
    },
    @{
        Path = 'site/index.html'
        Pattern = 'OTP Harbor \d+\.\d+\.\d+ is now publicly available from Microsoft Store'
        Replacement = "OTP Harbor $displayVersion is now publicly available from Microsoft Store"
    },
    @{
        Path = 'packaging/windows-store/STORE_LISTING.md'
        Pattern = '## Version \d+\.\d+\.\d+ release notes'
        Replacement = "## Version $displayVersion release notes"
    }
)

$expectedVersionJson = @"
{
  "displayVersion": "$displayVersion",
  "packageVersion": "$PackageVersion"
}
"@ + [Environment]::NewLine

$contentByPath = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
$contentByPath[$versionPath] = $expectedVersionJson

foreach ($update in $updates) {
    $path = Join-Path $resolvedRoot $update.Path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required public-version target is missing: $($update.Path)"
    }

    $content = if ($contentByPath.ContainsKey($path)) {
        $contentByPath[$path]
    } else {
        [IO.File]::ReadAllText($path)
    }
    $matches = [Text.RegularExpressions.Regex]::Matches($content, $update.Pattern)
    if ($matches.Count -ne 1) {
        throw "Expected exactly one Store-version marker in $($update.Path), found $($matches.Count)."
    }

    $contentByPath[$path] = [Text.RegularExpressions.Regex]::Replace(
        $content,
        $update.Pattern,
        $update.Replacement,
        [Text.RegularExpressions.RegexOptions]::None,
        [TimeSpan]::FromSeconds(2))
}

$changes = @($contentByPath.GetEnumerator() | ForEach-Object {
    [pscustomobject]@{ Path = $_.Key; Content = $_.Value }
})
$drifted = @($changes | Where-Object {
    -not (Test-Path -LiteralPath $_.Path -PathType Leaf) -or
    [IO.File]::ReadAllText($_.Path) -cne $_.Content
})

if ($Check) {
    if ($drifted.Count -gt 0) {
        $relativePaths = $drifted.Path | ForEach-Object { [IO.Path]::GetRelativePath($resolvedRoot, $_) }
        throw "Public Microsoft Store version metadata is out of sync: $($relativePaths -join ', ')"
    }

    Write-Output "Public Microsoft Store version metadata is synchronized at $displayVersion ($PackageVersion)."
    return
}

foreach ($change in $drifted) {
    [IO.File]::WriteAllText($change.Path, $change.Content, [Text.UTF8Encoding]::new($false))
}

Write-Output "Updated public Microsoft Store version metadata to $displayVersion ($PackageVersion)."
