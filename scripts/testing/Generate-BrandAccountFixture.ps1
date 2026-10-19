[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$IconPackPath,
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\..\artifacts\test-data\brand-accounts-600.json"),
    [ValidateRange(1, 5000)]
    [int]$Count = 600
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function ConvertTo-Base32 {
    param([Parameter(Mandatory)][byte[]]$Bytes)

    $alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
    $builder = [System.Text.StringBuilder]::new()
    $buffer = 0
    $bits = 0
    foreach ($value in $Bytes) {
        $buffer = ($buffer -shl 8) -bor $value
        $bits += 8
        while ($bits -ge 5) {
            $bits -= 5
            [void]$builder.Append($alphabet[($buffer -shr $bits) -band 31])
        }
        $buffer = $buffer -band ((1 -shl $bits) - 1)
    }
    if ($bits -gt 0) {
        [void]$builder.Append($alphabet[($buffer -shl (5 - $bits)) -band 31])
    }
    return $builder.ToString()
}

function Get-DeterministicBytes {
    param([Parameter(Mandatory)][string]$Value)

    return [System.Security.Cryptography.SHA256]::HashData(
        [System.Text.Encoding]::UTF8.GetBytes($Value))
}

$resolvedPackPath = [System.IO.Path]::GetFullPath($IconPackPath)
$stream = [System.IO.File]::Open(
    $resolvedPackPath,
    [System.IO.FileMode]::Open,
    [System.IO.FileAccess]::Read,
    [System.IO.FileShare]::Read)
try {
    $archive = [System.IO.Compression.ZipArchive]::new(
        $stream,
        [System.IO.Compression.ZipArchiveMode]::Read,
        $false)
    try {
        $manifestEntries = @($archive.Entries | Where-Object { $_.FullName -ceq "pack.json" })
        if ($manifestEntries.Count -ne 1) {
            throw "The OTP Harbor icon pack must contain exactly one root pack.json."
        }

        $manifestEntry = $manifestEntries[0]
        if ($manifestEntry.Length -le 0 -or $manifestEntry.Length -gt (128 * 1024 * 1024)) {
            throw "The OTP Harbor icon pack manifest has an invalid size."
        }

        $manifestStream = $manifestEntry.Open()
        try {
            $reader = [System.IO.StreamReader]::new(
                $manifestStream,
                [System.Text.Encoding]::UTF8,
                $true,
                4096,
                $false)
            try {
                $manifest = $reader.ReadToEnd() | ConvertFrom-Json -Depth 64
            }
            finally {
                $reader.Dispose()
            }
        }
        finally {
            $manifestStream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    $stream.Dispose()
}

if ($manifest.formatVersion -ne 1 -or $manifest.packId -cne "otp-harbor-icons") {
    throw "The selected file is not a supported OTP Harbor icon pack."
}

$brands = @(
    $manifest.brands |
        Where-Object {
            $displayName = [string]$_.displayName
            $aliases = @($_.issuerAliases | ForEach-Object { [string]$_ })
            -not [string]::IsNullOrWhiteSpace($displayName) -and $aliases -icontains $displayName
        } |
        Sort-Object -Property @{ Expression = { [string]$_.displayName }; Ascending = $true },
            @{ Expression = { [string]$_.id }; Ascending = $true }
)
if ($brands.Count -eq 0) {
    throw "The icon pack contains no brand display names that are registered as issuer aliases."
}

$accounts = for ($index = 0; $index -lt $Count; $index++) {
    $brand = $brands[$index % $brands.Count]
    $ordinal = $index + 1
    $brandId = [string]$brand.id
    $issuer = [string]$brand.displayName
    $identityBytes = Get-DeterministicBytes "otp-harbor-brand-load-id:${ordinal}:$brandId"
    $guidBytes = [byte[]]::new(16)
    [Array]::Copy($identityBytes, $guidBytes, 16)
    $secretBytes = Get-DeterministicBytes "otp-harbor-brand-load-secret:${ordinal}:$brandId"
    $totpBytes = [byte[]]::new(20)
    [Array]::Copy($secretBytes, $totpBytes, 20)

    [ordered]@{
        id = [Guid]::new($guidBytes)
        issuer = $issuer
        secret = ConvertTo-Base32 $totpBytes
        account_name = "brand-load-$($ordinal.ToString('0000'))@example.invalid"
        period = if (($ordinal % 10) -eq 0) { 60 } else { 30 }
        favorite = (($ordinal % 12) -eq 0)
    }
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutput
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$accounts | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $resolvedOutput -Encoding utf8NoBOM

Write-Host "Created $Count synthetic accounts across $($brands.Count) resolvable brands at $resolvedOutput"
Write-Host "Import the same icon pack into OTP Harbor before importing this account fixture."
