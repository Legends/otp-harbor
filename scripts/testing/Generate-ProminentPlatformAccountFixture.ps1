[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\..\artifacts\test-data\prominent-platforms-500.json"),
    [ValidateRange(1, 5000)]
    [int]$Count = 500,
    [string]$TrancoListId = "Y83YG"
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

if ($TrancoListId -notmatch '^[A-Z0-9]{5}$') {
    throw "The Tranco list id is invalid."
}

$overrides = @{
    "google.com" = "Google"
    "cloudflare.com" = "Cloudflare"
    "facebook.com" = "Facebook"
    "gstatic.com" = "Google Static"
    "amazonaws.com" = "AWS"
    "googleapis.com" = "Google APIs"
    "akamai.net" = "Akamai"
    "microsoft.com" = "Microsoft"
    "youtube.com" = "YouTube"
    "apple.com" = "Apple"
    "instagram.com" = "Instagram"
    "twitter.com" = "X (Twitter)"
    "linkedin.com" = "LinkedIn"
    "github.com" = "GitHub"
    "amazon.com" = "Amazon"
    "netflix.com" = "Netflix"
    "paypal.com" = "PayPal"
    "dropbox.com" = "Dropbox"
    "reddit.com" = "Reddit"
    "spotify.com" = "Spotify"
}

$temporaryPath = Join-Path ([System.IO.Path]::GetTempPath()) "otp-harbor-tranco-$TrancoListId.csv"
try {
    Invoke-WebRequest -Uri "https://tranco-list.eu/download/$TrancoListId/1000000" -OutFile $temporaryPath

    $domains = Get-Content -LiteralPath $temporaryPath -TotalCount $Count |
        ForEach-Object { ($_ -split ',', 2)[1].Trim().ToLowerInvariant() } |
        Where-Object { $_ -match '^[a-z0-9.-]+$' } |
        Select-Object -Unique
    if ($domains.Count -ne $Count) {
        throw "Expected $Count distinct ranked domains, found $($domains.Count)."
    }

    $usedIssuers = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    $accounts = for ($index = 0; $index -lt $domains.Count; $index++) {
        $domain = $domains[$index]
        $issuer = if ($overrides.ContainsKey($domain)) {
            $overrides[$domain]
        }
        else {
            $domain
        }
        if (-not $usedIssuers.Add($issuer)) {
            $issuer = "$issuer ($domain)"
            [void]$usedIssuers.Add($issuer)
        }

        $ordinal = $index + 1
        $identityBytes = Get-DeterministicBytes "otp-harbor-load-id:${ordinal}:$domain"
        $guidBytes = [byte[]]::new(16)
        [Array]::Copy($identityBytes, $guidBytes, 16)
        $secretBytes = Get-DeterministicBytes "otp-harbor-load-secret:${ordinal}:$domain"
        $totpBytes = [byte[]]::new(20)
        [Array]::Copy($secretBytes, $totpBytes, 20)

        [ordered]@{
            id = [Guid]::new($guidBytes)
            issuer = $issuer
            secret = ConvertTo-Base32 $totpBytes
            account_name = "load-test-$($ordinal.ToString('000'))@example.invalid"
            period = if (($ordinal % 10) -eq 0) { 60 } else { 30 }
            favorite = (($ordinal % 12) -eq 0)
        }
    }

    $resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
    $outputDirectory = Split-Path -Parent $resolvedOutput
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
    $accounts | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $resolvedOutput -Encoding utf8NoBOM
    Write-Host "Created $Count synthetic accounts from Tranco list $TrancoListId at $resolvedOutput"
}
finally {
    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}
