<#
.SYNOPSIS
Checks shared localization binaries in a published application or Android APK.
#>
[CmdletBinding()]
param(
    [string]$PublishDirectory,
    [string]$AndroidApkPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $PublishDirectory -and -not $AndroidApkPath) {
    throw 'Supply -PublishDirectory, -AndroidApkPath, or both.'
}

function Test-ManagedLocalizationBundle([string]$Directory) {
    foreach ($relativePath in @('TOTP.Localization.dll', 'de/TOTP.Localization.resources.dll',
            'fr/TOTP.Localization.resources.dll', 'es/TOTP.Localization.resources.dll')) {
        $path = Join-Path $Directory $relativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Shared localization binary is missing: $path"
        }
    }
    Write-Output "PASS: common localization library and all translated satellites are present in $Directory"
}

if ($PublishDirectory) {
    $resolvedPublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
    Test-ManagedLocalizationBundle $resolvedPublishDirectory
    $updaterDirectory = Join-Path $resolvedPublishDirectory 'TOTP.Updater'
    if (Test-Path -LiteralPath $updaterDirectory -PathType Container) {
        Test-ManagedLocalizationBundle $updaterDirectory
    }
}

if ($AndroidApkPath) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $apk = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $AndroidApkPath).Path)
    try {
        $stores = @($apk.Entries | Where-Object { $_.FullName -match '^lib/[^/]+/libassembly-store\.so$' })
        if ($stores.Count -eq 0) { throw 'The APK contains no supported managed assembly store.' }
        foreach ($store in $stores) {
            $stream = $store.Open()
            $buffer = [IO.MemoryStream]::new()
            try {
                $stream.CopyTo($buffer)
                $names = [Text.Encoding]::ASCII.GetString($buffer.ToArray())
                foreach ($name in @('TOTP.Localization.dll', 'de/TOTP.Localization.resources.dll',
                        'fr/TOTP.Localization.resources.dll', 'es/TOTP.Localization.resources.dll')) {
                    if (-not $names.Contains($name)) {
                        throw "The APK assembly store $($store.FullName) is missing $name"
                    }
                }
            }
            finally {
                $buffer.Dispose()
                $stream.Dispose()
            }
            Write-Output "PASS: $($store.FullName) retains common localization and all translated satellites after APK packaging."
        }
    }
    finally { $apk.Dispose() }
}
