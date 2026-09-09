<#
.SYNOPSIS
Validates completeness and format safety of every localized resource set.

.DESCRIPTION
Compares German, French, and Spanish desktop, mobile, and updater resources with their invariant catalogs; rejects missing, extra, empty, or duplicate entries and mismatched composite-format placeholders.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$groups = [ordered]@{
    Desktop = 'TOTP.UI.Avalonia.Desktop/Localization/Strings'
    Mobile = 'TOTP.UI.Avalonia.Mobile/Localization/Strings'
    Updater = 'TOTP.Updater/Localization/UpdaterStrings'
}
$cultures = @('de', 'fr', 'es')

function Read-ResourceMap {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Localization resource is missing: $Path"
    }
    [xml]$document = [IO.File]::ReadAllText($Path)
    $result = @{}
    foreach ($entry in $document.root.data) {
        $key = [string]$entry.name
        $value = [string]$entry.value
        if ([string]::IsNullOrWhiteSpace($key) -or [string]::IsNullOrWhiteSpace($value)) {
            throw "Localization resource contains an empty key or value: $Path"
        }
        if ($result.ContainsKey($key)) {
            throw "Localization resource contains a duplicate key '$key': $Path"
        }
        $result[$key] = $value
    }
    return $result
}

function Get-Placeholders {
    param([Parameter(Mandatory)][string]$Value)

    return @([regex]::Matches($Value, '\{[0-9]+\}') |
        ForEach-Object Value |
        Sort-Object -Unique)
}

foreach ($group in $groups.GetEnumerator()) {
    $basePath = Join-Path $repositoryRoot ($group.Value + '.resx')
    $base = Read-ResourceMap $basePath
    foreach ($culture in $cultures) {
        $localizedPath = Join-Path $repositoryRoot ($group.Value + ".$culture.resx")
        $localized = Read-ResourceMap $localizedPath
        $missing = @($base.Keys | Where-Object { -not $localized.ContainsKey($_) })
        $extra = @($localized.Keys | Where-Object { -not $base.ContainsKey($_) })
        if ($missing.Count -gt 0 -or $extra.Count -gt 0) {
            throw "$($group.Key) $culture resource keys differ from English. Missing: $($missing -join ', '); extra: $($extra -join ', ')"
        }
        foreach ($key in $base.Keys) {
            $expected = Get-Placeholders $base[$key]
            $actual = Get-Placeholders $localized[$key]
            if (($expected -join ',') -cne ($actual -join ',')) {
                throw "$($group.Key) $culture resource has mismatched placeholders for '$key'."
            }
        }
    }
    Write-Output "$($group.Key) localization is complete for English, German, French, and Spanish ($($base.Count) keys)."
}
