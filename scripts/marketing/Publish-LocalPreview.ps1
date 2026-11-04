[CmdletBinding()]
param([int]$Port = 8765, [switch]$NoServe)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$marketing = Join-Path $repo 'artifacts/marketing'
$preview = Join-Path $marketing 'staging/local-site'
$resolvedPreview = [System.IO.Path]::GetFullPath($preview)
$resolvedStaging = [System.IO.Path]::GetFullPath((Join-Path $marketing 'staging'))
if (-not $resolvedPreview.StartsWith($resolvedStaging + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Local preview target is outside marketing staging.'
}

& (Join-Path $PSScriptRoot 'Validate-MarketingAssets.ps1') -RequireCaptures
if (Test-Path -LiteralPath $resolvedPreview) {
    Remove-Item -LiteralPath $resolvedPreview -Recurse -Force
}
New-Item -ItemType Directory -Path (Join-Path $resolvedPreview 'assets/android'), (Join-Path $resolvedPreview 'assets/marketing'), (Join-Path $resolvedPreview 'android/guide') -Force | Out-Null

foreach ($name in @('index.html','styles.css','gallery.js','robots.txt','sitemap.xml','1b6f7ab9795743588fa8e24157ad1541.txt')) {
    Copy-Item -LiteralPath (Join-Path $repo "site/$name") -Destination (Join-Path $resolvedPreview $name)
}
Copy-Item -LiteralPath (Join-Path $repo 'site/android/index.html') -Destination (Join-Path $resolvedPreview 'android/index.html')
Copy-Item -LiteralPath (Join-Path $repo 'site/android/gallery.js') -Destination (Join-Path $resolvedPreview 'android/gallery.js')
Copy-Item -LiteralPath (Join-Path $repo 'site/android/guide/index.html') -Destination (Join-Path $resolvedPreview 'android/guide/index.html')
Copy-Item -LiteralPath (Join-Path $repo 'packaging/windows-store/screenshots/en-US/marketing/02-dark-account-list.png') -Destination (Join-Path $resolvedPreview 'assets/otp-harbor-totp-authenticator-windows.png')
Copy-Item -Path (Join-Path $repo 'packaging/android/marketing/en-US/*.png') -Destination (Join-Path $resolvedPreview 'assets/android')
Copy-Item -LiteralPath (Join-Path $repo 'OtpHarbor.UI.Avalonia.Desktop/Assets/Icons/app-1024.png') -Destination (Join-Path $resolvedPreview 'assets/app-icon.png')

# The local preview deliberately includes reviewed candidates before the public Pages gate is opened.
Copy-Item -LiteralPath (Join-Path $marketing 'website/gallery.json') -Destination (Join-Path $resolvedPreview 'assets/marketing/gallery.json')
Copy-Item -Path (Join-Path $marketing 'website/optimized/*') -Destination (Join-Path $resolvedPreview 'assets/marketing') -Recurse

if ($NoServe) {
    Write-Host "Local preview refreshed: $resolvedPreview"
    return
}
$listener = Get-NetTCPConnection -LocalAddress '127.0.0.1' -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if ($listener) {
    $owner = Get-CimInstance Win32_Process -Filter "ProcessId = $($listener[0].OwningProcess)"
    if ($owner.CommandLine -match 'http\.server' -and $owner.CommandLine.Contains($resolvedPreview)) {
        Write-Host "Local preview refreshed: http://127.0.0.1:$Port/"
        Write-Host "Android page: http://127.0.0.1:$Port/android/"
        return
    }
    throw "Port $Port is used by another process. Preview created at $resolvedPreview; rerun with another -Port."
}
$arguments = @('-m','http.server',"$Port",'--bind','127.0.0.1','--directory',('"' + $resolvedPreview + '"'))
$server = Start-Process -FilePath 'python' -ArgumentList $arguments -WindowStyle Hidden -PassThru
Start-Sleep -Milliseconds 800
if ($server.HasExited) { throw 'Local preview server exited unexpectedly.' }
Write-Host "Local preview: http://127.0.0.1:$Port/"
Write-Host "Android page: http://127.0.0.1:$Port/android/"
Write-Host "Preview files: $resolvedPreview"
Write-Host "Server process ID: $($server.Id)"
