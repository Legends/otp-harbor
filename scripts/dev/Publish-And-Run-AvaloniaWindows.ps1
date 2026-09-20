<#
.SYNOPSIS
Publishes and starts a local Windows Avalonia build of OTP Harbor.

.DESCRIPTION
Publishes the desktop project for the selected Windows runtime into artifacts/dev by default, stops an existing instance so the newly published single-instance app is used, validates the produced executable, and launches it from the publish directory. Pass -StopRunningInstance:$false only when intentionally publishing without replacing the running app.
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$Runtime = "win-x64",

    [string]$OutputPath,

    [switch]$SelfContained,

    [switch]$StopRunningInstance = $true
)

$ErrorActionPreference = "Stop"

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptDirectory "..\.."))
$projectPath = Join-Path $repositoryRoot "TOTP.UI.Avalonia.Desktop\TOTP.UI.Avalonia.Desktop.csproj"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repositoryRoot "artifacts\dev\avalonia-windows"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $repositoryRoot $OutputPath
}

$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$executablePath = Join-Path $OutputPath "TOTP.UI.Avalonia.Desktop.exe"

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Avalonia desktop project not found: $projectPath"
}

if ($StopRunningInstance) {
    Get-Process -Name "TOTP.UI.Avalonia.Desktop" -ErrorAction SilentlyContinue |
        ForEach-Object {
            Write-Host "Stopping running OTP Harbor Avalonia process $($_.Id)..."
            Stop-Process -Id $_.Id -ErrorAction Stop
            $_.WaitForExit(5000)
        }
}

$selfContainedValue = if ($SelfContained) { "true" } else { "false" }

Write-Host "Publishing OTP Harbor Avalonia for Windows ($Configuration, $Runtime)..."
& dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained $selfContainedValue `
    --nologo `
    --verbosity minimal `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    --output $OutputPath

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Published executable not found: $executablePath"
}

$publishedExecutable = Get-Item -LiteralPath $executablePath
$publishedHash = (Get-FileHash -LiteralPath $executablePath -Algorithm SHA256).Hash
Write-Host "Published: $($publishedExecutable.FullName)"
Write-Host "Timestamp: $($publishedExecutable.LastWriteTime.ToString('O'))"
Write-Host "SHA-256: $publishedHash"
Write-Host "Starting $executablePath"
Start-Process -FilePath $executablePath -WorkingDirectory $OutputPath
