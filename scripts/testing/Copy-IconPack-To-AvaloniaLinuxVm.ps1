<#
.SYNOPSIS
Copies the OTP Harbor icon pack from Windows to the Ubuntu test VM Downloads folder.

.DESCRIPTION
Creates the remote Downloads directory when necessary and transfers the selected
.otphicons file to /home/<VM user>/Downloads/otp-harbor-icons.otphicons over SSH.

.EXAMPLE
.\scripts\testing\Copy-IconPack-To-AvaloniaLinuxVm.ps1

.EXAMPLE
.\scripts\testing\Copy-IconPack-To-AvaloniaLinuxVm.ps1 `
    -SshIdentityFile $env:USERPROFILE\.ssh\id_ed25519
#>
[CmdletBinding()]
param(
    [string]$SourceFile = (
        Join-Path $env:USERPROFILE "Downloads\otp-harbor-icons.otphicons"),
    [string]$VmHost = "192.168.250.10",
    [string]$VmUser = "bushido",
    [string]$SshIdentityFile
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

foreach ($commandName in @("ssh", "scp")) {
    if ($null -eq (Get-Command $commandName -CommandType Application `
            -ErrorAction SilentlyContinue | Select-Object -First 1)) {
        throw "OpenSSH command '$commandName' was not found on PATH."
    }
}

if (-not (Test-Path -LiteralPath $SourceFile -PathType Leaf)) {
    throw "The icon-pack file '$SourceFile' was not found."
}

$resolvedSourceFile = (Resolve-Path -LiteralPath $SourceFile).Path
$sshOptions = [Collections.Generic.List[string]]::new()
$sshOptions.Add("-o")
$sshOptions.Add("ConnectTimeout=15")

if (-not [string]::IsNullOrWhiteSpace($SshIdentityFile)) {
    if (-not (Test-Path -LiteralPath $SshIdentityFile -PathType Leaf)) {
        throw "The SSH identity file '$SshIdentityFile' was not found."
    }

    $sshOptions.Add("-i")
    $sshOptions.Add((Resolve-Path -LiteralPath $SshIdentityFile).Path)
}

$destination = "${VmUser}@${VmHost}"
$remoteDirectory = "/home/$VmUser/Downloads"
$remoteFile = "$remoteDirectory/otp-harbor-icons.otphicons"

Write-Host "Ensuring $remoteDirectory exists on $destination..."
& ssh @sshOptions $destination 'mkdir -p -- "$HOME/Downloads"'
if ($LASTEXITCODE -ne 0) {
    throw "Creating the remote Downloads directory failed with exit code $LASTEXITCODE."
}

Write-Host "Copying $resolvedSourceFile to ${destination}:$remoteFile..."
& scp @sshOptions $resolvedSourceFile "${destination}:$remoteFile"
if ($LASTEXITCODE -ne 0) {
    throw "Copying the icon pack failed with exit code $LASTEXITCODE."
}

Write-Host "Icon pack copied to $remoteFile."
