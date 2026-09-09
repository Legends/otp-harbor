<#
.SYNOPSIS
Sanitizes an Avalonia publish directory before release packaging.

.DESCRIPTION
Verifies the application host and settings file, rejects stale updater publish subtrees, safely removes debug-symbol files contained by the publish root, and confirms no PDB files remain.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path -LiteralPath $PublishDirectory).Path
$windowsHost = Join-Path $root "TOTP.UI.Avalonia.Desktop.exe"
$unixHost = Join-Path $root "TOTP.UI.Avalonia.Desktop"
$missingHost = -not (Test-Path -LiteralPath $windowsHost -PathType Leaf) -and
    -not (Test-Path -LiteralPath $unixHost -PathType Leaf)
if ($missingHost) {
    throw "The release publish directory does not contain the Avalonia host."
}
if (-not (Test-Path -LiteralPath (Join-Path $root "appsettings.json") -PathType Leaf)) {
    throw "The release publish directory does not contain appsettings.json."
}

foreach ($relativePath in @(
    "TOTP.Updater/ref",
    "TOTP.Updater/publish",
    "TOTP.Updater/win-x64")) {
    if (Test-Path -LiteralPath (Join-Path $root $relativePath)) {
        throw "The release publish contains a stale updater subtree: $relativePath"
    }
}

$symbols = @(Get-ChildItem -LiteralPath $root -File -Recurse -Filter "*.pdb")
foreach ($symbol in $symbols) {
    $relative = [IO.Path]::GetRelativePath($root, $symbol.FullName)
    $invalidSymbol = $relative.StartsWith("..", [StringComparison]::Ordinal) -or
        [IO.Path]::IsPathRooted($relative) -or
        ($symbol.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
    if ($invalidSymbol) {
        throw "A debug-symbol path is outside the regular release payload."
    }
    Remove-Item -LiteralPath $symbol.FullName -Force
}

if (Get-ChildItem -LiteralPath $root -File -Recurse -Filter "*.pdb" | Select-Object -First 1) {
    throw "Debug symbols remain in the release payload."
}
Write-Output "Removed $($symbols.Count) debug-symbol file(s) from the release payload."
