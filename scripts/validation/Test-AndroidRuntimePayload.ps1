#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$payloadValidator = Join-Path $PSScriptRoot '../release/android/Test-AndroidRuntimePayload.ps1'
$payloadTempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$payloadTestRoot = Join-Path $payloadTempBase ('otp-harbor-aot-payload-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $payloadTestRoot | Out-Null

function New-PayloadFixture([string]$Name, [string]$Runtime = 'NativeAOT', [switch]$WrongElf, [switch]$MissingX64, [switch]$MixedRuntime) {
    $path = Join-Path $payloadTestRoot "$Name.apk"
    $zip = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $abis = if ($MissingX64) { @('arm64-v8a') } else { @('arm64-v8a', 'x86_64') }
        foreach ($abi in $abis) {
            $header = [byte[]]::new(20)
            $header[0] = 127; $header[1] = 69; $header[2] = 76; $header[3] = 70
            $header[4] = 2; $header[5] = 1
            $header[18] = if ($abi -eq 'arm64-v8a' -and -not $WrongElf) { 183 } else { 62 }
            $name = if ($Runtime -eq 'NativeAOT') { 'libTOTP.UI.Avalonia.Android.so' } else { 'libmonosgen-2.0.so' }
            $stream = $zip.CreateEntry("lib/$abi/$name").Open()
            try { $stream.Write($header) } finally { $stream.Dispose() }
            if ($Runtime -eq 'Mono' -or $MixedRuntime) {
                $stream = $zip.CreateEntry("lib/$abi/libaot-Synthetic.dll.so").Open()
                try { $stream.Write($header) } finally { $stream.Dispose() }
            }
        }
    }
    finally { $zip.Dispose() }
    return $path
}

function Assert-PayloadRejected([string]$Path, [string]$Runtime) {
    try { & $payloadValidator -ApkPath $Path -Runtime $Runtime | Out-Null }
    catch { return }
    throw "An invalid $Runtime payload was accepted."
}

try {
    $native = New-PayloadFixture 'native'
    $mono = New-PayloadFixture 'mono' -Runtime Mono
    & $payloadValidator -ApkPath $native -Runtime NativeAOT
    & $payloadValidator -ApkPath $mono -Runtime Mono
    Assert-PayloadRejected (New-PayloadFixture 'wrong-elf' -WrongElf) NativeAOT
    Assert-PayloadRejected (New-PayloadFixture 'missing-x64' -MissingX64) NativeAOT
    Assert-PayloadRejected (New-PayloadFixture 'mixed' -MixedRuntime) NativeAOT
    Assert-PayloadRejected $mono NativeAOT
    Assert-PayloadRejected $native Mono
    $empty = Join-Path $payloadTestRoot 'empty.apk'
    $zip = [IO.Compression.ZipFile]::Open($empty, [IO.Compression.ZipArchiveMode]::Create)
    $zip.Dispose()
    Assert-PayloadRejected $empty NativeAOT
    Write-Output 'Android runtime payload regression checks passed (2 valid, 6 rejected).'
}
finally {
    $payloadCleanup = [IO.Path]::GetFullPath($payloadTestRoot)
    if ([IO.Path]::GetDirectoryName($payloadCleanup) -ne $payloadTempBase.TrimEnd([IO.Path]::DirectorySeparatorChar) -or
        [IO.Path]::GetFileName($payloadCleanup) -notlike 'otp-harbor-aot-payload-test-*') {
        throw 'Invalid payload test cleanup target.'
    }
    Remove-Item -LiteralPath $payloadCleanup -Recurse -Force
}
