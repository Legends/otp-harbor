<#
.SYNOPSIS
Validates runtime libraries and ELF architectures inside an Android APK.
.DESCRIPTION
Checks payload structure only. The caller must separately verify the APK signature and manifest.
#>
param(
    [Parameter(Mandatory)][string]$ApkPath,
    [Parameter(Mandatory)][ValidateSet('Mono', 'NativeAOT')][string]$Runtime,
    [ValidateSet('arm64-v8a', 'x86_64')][string[]]$Architectures = @('arm64-v8a', 'x86_64')
)
$ErrorActionPreference = 'Stop'
if ($Architectures.Count -eq 0 -or @($Architectures | Select-Object -Unique).Count -ne $Architectures.Count) {
    throw 'Expected APK architectures must be non-empty and unique.'
}
$payloadArchive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $ApkPath).Path)
try {
    $payloadLibraries = @($payloadArchive.Entries | Where-Object { $_.FullName -match '^lib/[^/]+/[^/]+\.so$' })
    $payloadAbis = @($payloadLibraries | ForEach-Object { $_.FullName.Split('/')[1] } | Sort-Object -Unique)
    if (Compare-Object ($Architectures | Sort-Object) $payloadAbis) { throw 'APK runtime architectures do not match the expected set.' }
    foreach ($payloadAbi in $Architectures) {
        $payloadName = if ($Runtime -eq 'NativeAOT') { 'libTOTP.UI.Avalonia.Android.so' } else { 'libmonosgen-2.0.so' }
        $payloadRuntime = @($payloadLibraries | Where-Object { $_.FullName -ceq "lib/$payloadAbi/$payloadName" })
        if ($payloadRuntime.Count -ne 1 -or $payloadRuntime[0].Length -lt 20) { throw "The $Runtime library is missing or empty for $payloadAbi." }
        $payloadHeader = [byte[]]::new(20)
        $payloadStream = $payloadRuntime[0].Open()
        try { $payloadStream.ReadExactly($payloadHeader) }
        finally { $payloadStream.Dispose() }
        $payloadMachine = [int]$payloadHeader[18] + ([int]$payloadHeader[19] * 256)
        $expectedMachine = if ($payloadAbi -eq 'arm64-v8a') { 183 } else { 62 }
        if ($payloadHeader[0] -ne 127 -or $payloadHeader[1] -ne 69 -or $payloadHeader[2] -ne 76 -or $payloadHeader[3] -ne 70 -or
            $payloadHeader[4] -ne 2 -or $payloadHeader[5] -ne 1 -or $payloadMachine -ne $expectedMachine) {
            throw "The runtime library has an invalid ELF architecture for $payloadAbi."
        }
        if ($Runtime -eq 'Mono' -and -not ($payloadLibraries | Where-Object { $_.FullName -match "^lib/$payloadAbi/libaot-.+\.dll\.so$" })) {
            throw "The Mono APK has no AOT libraries for $payloadAbi."
        }
    }
    if ($Runtime -eq 'NativeAOT' -and ($payloadLibraries | Where-Object { $_.FullName -match '/(?:libmonosgen-2\.0|libaot-.+\.dll)\.so$' })) {
        throw 'The NativeAOT APK contains unexpected Mono runtime libraries.'
    }
    Write-Output "Validated $Runtime runtime payload for $($Architectures -join ', ')."
}
finally { $payloadArchive.Dispose() }
