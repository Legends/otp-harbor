[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$maximumArchiveBytes = 25MB
$apiBase = 'https://api.github.com/repos'
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $true
$client = [Net.Http.HttpClient]::new($handler)
$client.DefaultRequestHeaders.UserAgent.ParseAdd('OTP-Harbor-Icon-Pack-Compatibility')
$client.DefaultRequestHeaders.Accept.ParseAdd('application/vnd.github+json')
$client.DefaultRequestHeaders.Add('X-GitHub-Api-Version', '2022-11-28')
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
    $client.DefaultRequestHeaders.Authorization =
        [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $env:GITHUB_TOKEN)
}

function Get-LatestRelease([string]$Repository) {
    $json = $client.GetStringAsync("$apiBase/$Repository/releases/latest").GetAwaiter().GetResult()
    $release = $json | ConvertFrom-Json -Depth 20
    if ([string]::IsNullOrWhiteSpace($release.tag_name) -or $release.tag_name.Length -gt 128) {
        throw "GitHub returned an invalid latest-release tag for $Repository."
    }
    return $release
}

function Save-BoundedDownload([Uri]$Uri, [string]$Destination) {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, $Uri)
    $response = $null
    $input = $null
    $output = $null
    try {
        $response = $client.SendAsync(
            $request,
            [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        $response.EnsureSuccessStatusCode() | Out-Null
        if ($response.Content.Headers.ContentLength -gt $maximumArchiveBytes) {
            throw "The upstream archive exceeds the supported compressed size limit."
        }

        $input = $response.Content.ReadAsStream()
        $output = [IO.File]::Open($Destination, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $buffer = [byte[]]::new(81920)
        [long]$total = 0
        while (($read = $input.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $total += $read
            if ($total -gt $maximumArchiveBytes) {
                throw "The upstream archive exceeds the supported compressed size limit."
            }
            $output.Write($buffer, 0, $read)
        }
        if ($total -eq 0) {
            throw "The upstream archive download was empty."
        }
    }
    catch {
        if ($null -ne $output) { $output.Dispose(); $output = $null }
        if ([IO.File]::Exists($Destination)) { [IO.File]::Delete($Destination) }
        throw
    }
    finally {
        if ($null -ne $output) { $output.Dispose() }
        if ($null -ne $input) { $input.Dispose() }
        if ($null -ne $response) { $response.Dispose() }
        $request.Dispose()
    }
}

function Write-WorkflowOutput([string]$Name, [string]$Value) {
    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
        "$Name=$Value" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    }
}

try {
    $aegisRelease = Get-LatestRelease 'aegis-icons/aegis-icons'
    $aegisAssets = @($aegisRelease.assets) |
        Where-Object { $_.name -eq 'aegis-icons.zip' }
    if ($aegisAssets.Count -ne 1) {
        throw 'The latest aegis-icons release does not provide aegis-icons.zip.'
    }
    $aegisAsset = $aegisAssets[0]
    if ([string]::IsNullOrWhiteSpace($aegisAsset.browser_download_url)) {
        throw 'The latest aegis-icons release asset has no download URL.'
    }

    $simpleIconsRelease = Get-LatestRelease 'simple-icons/simple-icons'
    $simpleIconsTag = [string]$simpleIconsRelease.tag_name
    $escapedSimpleIconsTag = [Uri]::EscapeDataString($simpleIconsTag)

    $aegisPath = Join-Path $resolvedOutput 'aegis-icons-latest.zip'
    $simpleIconsPath = Join-Path $resolvedOutput 'simple-icons-latest.zip'
    Save-BoundedDownload ([Uri]$aegisAsset.browser_download_url) $aegisPath
    Save-BoundedDownload ([Uri]"https://github.com/simple-icons/simple-icons/archive/refs/tags/$escapedSimpleIconsTag.zip") $simpleIconsPath

    $metadata = [ordered]@{
        downloadedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        aegis = [ordered]@{
            repository = 'aegis-icons/aegis-icons'
            version = [string]$aegisRelease.tag_name
            file = [IO.Path]::GetFileName($aegisPath)
            sha256 = (Get-FileHash -LiteralPath $aegisPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        simpleIcons = [ordered]@{
            repository = 'simple-icons/simple-icons'
            version = $simpleIconsTag
            file = [IO.Path]::GetFileName($simpleIconsPath)
            sha256 = (Get-FileHash -LiteralPath $simpleIconsPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $metadata | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath (Join-Path $resolvedOutput 'upstream-metadata.json') -Encoding utf8

    Write-WorkflowOutput 'aegis_path' $aegisPath
    Write-WorkflowOutput 'aegis_version' ([string]$aegisRelease.tag_name)
    Write-WorkflowOutput 'simple_icons_path' $simpleIconsPath
    Write-WorkflowOutput 'simple_icons_version' $simpleIconsTag
    Write-Host "Downloaded aegis-icons $($aegisRelease.tag_name) and Simple Icons $simpleIconsTag."
}
finally {
    $client.Dispose()
    $handler.Dispose()
}
