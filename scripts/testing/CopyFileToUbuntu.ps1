

<#
.SYNOPSIS
Copies any file from Windows to the Ubuntu VM Downloads folder.

.DESCRIPTION
Creates the remote Downloads directory if necessary and transfers
the specified file over SSH, preserving its original filename.

.EXAMPLE
.\dildo.ps1 "C:\Users\Admin\Downloads\myfile.zip"

.EXAMPLE
.\dildo.ps1 "C:\Users\Admin\Downloads\icons.otphicons" `
    -SshIdentityFile "$env:USERPROFILE\.ssh\id_ed25519"

.EXAMPLE
.\dildo.ps1 -SourceFile "C:\Temp\test.txt"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateNotNullOrEmpty()]
    [string]$SourceFile,

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
    throw "Source file '$SourceFile' was not found."
}

$resolvedSourceFile = (Resolve-Path -LiteralPath $SourceFile).Path
$fileName = [System.IO.Path]::GetFileName($resolvedSourceFile)

$sshOptions = [Collections.Generic.List[string]]::new()
$sshOptions.Add("-o")
$sshOptions.Add("ConnectTimeout=15")

if (-not [string]::IsNullOrWhiteSpace($SshIdentityFile)) {
    if (-not (Test-Path -LiteralPath $SshIdentityFile -PathType Leaf)) {
        throw "SSH identity file '$SshIdentityFile' was not found."
    }

    $sshOptions.Add("-i")
    $sshOptions.Add((Resolve-Path -LiteralPath $SshIdentityFile).Path)
}

$destination = "${VmUser}@${VmHost}"
$remoteDirectory = "/home/$VmUser/Downloads"
$remoteFile = "$remoteDirectory/$fileName"

Write-Host "Ensuring $remoteDirectory exists on $destination..."

& ssh @sshOptions $destination 'mkdir -p -- "$HOME/Downloads"'

if ($LASTEXITCODE -ne 0) {
    throw "Creating the remote Downloads directory failed with exit code $LASTEXITCODE."
}

Write-Host "Copying $resolvedSourceFile to ${destination}:$remoteFile..."

& scp @sshOptions $resolvedSourceFile "${destination}:$remoteFile"

if ($LASTEXITCODE -ne 0) {
    throw "Copying file failed with exit code $LASTEXITCODE."
}

Write-Host "File successfully copied to $remoteFile."
