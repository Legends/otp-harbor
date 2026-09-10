<#
.SYNOPSIS
Builds the custom Avalonia OTP Harbor setup around the validated Windows MSI.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$MsiPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(?:-rc\d+)?$')][string]$ReleaseVersion,
    [string]$WixExecutable = 'wix',
    [string]$DotNetExecutable = 'dotnet'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$resolvedMsi = (Resolve-Path -LiteralPath $MsiPath).Path
$resourceRoot = Join-Path $PSScriptRoot 'installer'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
$outputPath = Join-Path $resolvedOutput "OTP-Harbor-windows-x64-setup-$ReleaseVersion.exe"
$installerUiDirectory = Join-Path ([IO.Path]::GetTempPath()) ("otp-harbor-installer-ui-" + [Guid]::NewGuid().ToString('N'))

try {
    & $DotNetExecutable publish (Join-Path $repoRoot 'TOTP.Installer/TOTP.Installer.csproj') `
        -c Release `
        -r win-x64 `
        --self-contained true `
        --nologo `
        -p:Version=$ReleaseVersion `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugSymbols=false `
        -p:DebugType=None `
        -o $installerUiDirectory
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $installerUiDirectory 'TOTP.Installer.exe'))) {
        throw 'The custom OTP Harbor installer UI could not be published.'
    }

    $payloadIncludePath = Join-Path $installerUiDirectory 'InstallerPayloads.wxi'
    $payloadLines = @('<Include xmlns="http://wixtoolset.org/schemas/v4/wxs">')
    $payloadLines += Get-ChildItem -LiteralPath $installerUiDirectory -File -Recurse |
        Where-Object { $_.Name -ne 'TOTP.Installer.exe' -and $_.Extension -notin @('.pdb', '.wxi') } |
        Sort-Object FullName |
        ForEach-Object {
            $source = [Security.SecurityElement]::Escape($_.FullName)
            "  <Payload SourceFile=`"$source`" />"
        }
    $payloadLines += '</Include>'
    [IO.File]::WriteAllLines($payloadIncludePath, $payloadLines, [Text.UTF8Encoding]::new($false))

    & $WixExecutable build -arch x64 `
        -ext WixToolset.BootstrapperApplications.wixext/5.0.2 `
        -ext WixToolset.Util.wixext/5.0.2 `
        -b "InstallerUi=$installerUiDirectory" `
        -d "ReleaseVersion=$ReleaseVersion" `
        -d "MsiPath=$resolvedMsi" `
        -d "InstallerPayloads=$payloadIncludePath" `
        -d "ApplicationIcon=$(Join-Path $repoRoot 'TOTP.UI.Avalonia.Desktop/Assets/Icons/app.ico')" `
        -o $outputPath `
        (Join-Path $resourceRoot 'HarborSetup.wxs')
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $outputPath -PathType Leaf)) {
        throw 'The OTP Harbor setup bundle could not be built.'
    }
}
finally {
    if (Test-Path -LiteralPath $installerUiDirectory) {
        Remove-Item -LiteralPath $installerUiDirectory -Recurse -Force
    }
}
Write-Output $outputPath
