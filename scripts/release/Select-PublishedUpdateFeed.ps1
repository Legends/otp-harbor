param(
    [Parameter(Mandatory = $true)]
    [string]$ReleasesJsonPath
)

$ErrorActionPreference = "Stop"
$resolvedReleases = (Resolve-Path -LiteralPath $ReleasesJsonPath).Path
$releases = @(Get-Content -LiteralPath $resolvedReleases -Raw | ConvertFrom-Json)

$candidates = foreach ($release in $releases) {
    if ($release.draft -or
        [string]$release.tag_name -notmatch '^v(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-rc(?<rc>\d+))?$') {
        continue
    }

    $revision = if ($Matches.rc) { [int]$Matches.rc } else { 65535 }
    if ($revision -gt 65535 -or ($Matches.rc -and $revision -eq 0)) {
        continue
    }
    $assetNames = @($release.assets | Select-Object -ExpandProperty name)
    if ($assetNames -notcontains 'appcast-v2.xml' -or
        $assetNames -notcontains 'appcast-v2.xml.signature') {
        continue
    }

    [PSCustomObject]@{
        Tag = [string]$release.tag_name
        Version = [Version]::new(
            [int]$Matches.major,
            [int]$Matches.minor,
            [int]$Matches.patch,
            $revision)
    }
}

$selected = $candidates | Sort-Object Version -Descending | Select-Object -First 1
if ($null -ne $selected) {
    Write-Output $selected.Tag
}
