<#
.SYNOPSIS
Synchronizes the Windows working tree into an Ubuntu VM-local directory, restores it,
and starts the Avalonia desktop application in the VM's active desktop session.

.DESCRIPTION
Packages the current working tree without build outputs or Git metadata, transfers it by SSH, synchronizes a guarded VM-local source directory, restores the desktop project, imports the active GNOME/Xwayland or XFCE session environment, and runs a locally versioned Release or Debug build. A configured identity file makes transfer and execution non-interactive.

.EXAMPLE
.\scripts\testing\Sync-Restore-Run-AvaloniaLinuxVm.ps1

.EXAMPLE
.\scripts\testing\Sync-Restore-Run-AvaloniaLinuxVm.ps1 -VmHost 192.168.1.50

.EXAMPLE
.\scripts\testing\Sync-Restore-Run-AvaloniaLinuxVm.ps1 -MountedRepository /mnt/otp-harbor

.EXAMPLE
.\scripts\testing\Sync-Restore-Run-AvaloniaLinuxVm.ps1 -SshIdentityFile $env:USERPROFILE\.ssh\id_ed25519
#>
[CmdletBinding()]
param(
    [string]$VmHost = "192.168.250.10",
    [string]$VmName = "Ubuntu 26.04",
    [string]$VmUser = "bushido",
    [string]$PreferredNetworkAdapter = "Stable RDP",
    [string]$SshIdentityFile,
    [string]$GitExecutable,
    [string]$LocalRepository,
    [string]$MountedRepository,
    [string]$VmRepository = "~/source/otp-harbor",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-GitExecutable {
    param([string]$RequestedExecutable)

    if (-not [string]::IsNullOrWhiteSpace($RequestedExecutable)) {
        if (-not (Test-Path -LiteralPath $RequestedExecutable -PathType Leaf)) {
            throw "The Git executable '$RequestedExecutable' was not found."
        }

        return (Resolve-Path -LiteralPath $RequestedExecutable).Path
    }

    $pathCommand = Get-Command git -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -ne $pathCommand) {
        return $pathCommand.Source
    }

    $candidates = [Collections.Generic.List[string]]::new()
    $programFiles = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::ProgramFiles)
    $programFilesX86 = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::ProgramFilesX86)
    $localApplicationData = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::LocalApplicationData)
    $candidates.Add((Join-Path $programFiles "Git\cmd\git.exe"))
    $candidates.Add((Join-Path $localApplicationData "Programs\Git\cmd\git.exe"))

    $vsWhere = Join-Path $programFilesX86 "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path -LiteralPath $vsWhere -PathType Leaf) {
        $visualStudioInstallations = @(
            & $vsWhere -products * -all -property installationPath 2>$null)
        foreach ($installation in $visualStudioInstallations) {
            if ([string]::IsNullOrWhiteSpace($installation)) { continue }
            $candidates.Add((Join-Path $installation (
                "Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe")))
        }
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "Git is required to derive the local VM build version. Install Git, add it to PATH, or supply -GitExecutable."
}

if (-not (Get-Command ssh -ErrorAction SilentlyContinue)) {
    throw "OpenSSH client (ssh.exe) is required on the Windows host."
}
$GitExecutable = Resolve-GitExecutable $GitExecutable

if ([string]::IsNullOrWhiteSpace($SshIdentityFile)) {
    $defaultIdentityFile = Join-Path (
        [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) ".ssh\legends"
    if (Test-Path -LiteralPath $defaultIdentityFile -PathType Leaf) {
        $SshIdentityFile = $defaultIdentityFile
    }
}
elseif (-not (Test-Path -LiteralPath $SshIdentityFile -PathType Leaf)) {
    throw "The SSH identity file '$SshIdentityFile' was not found."
}

$sshConnectionOptions = @("-o", "ConnectTimeout=10")
if (-not [string]::IsNullOrWhiteSpace($SshIdentityFile)) {
    $SshIdentityFile = (Resolve-Path -LiteralPath $SshIdentityFile).Path
    $sshConnectionOptions += @(
        "-i", $SshIdentityFile,
        "-o", "IdentitiesOnly=yes",
        "-o", "BatchMode=yes")
}

if (-not [string]::IsNullOrWhiteSpace($MountedRepository) -and
    -not [string]::IsNullOrWhiteSpace($LocalRepository)) {
    throw "Specify either -LocalRepository or -MountedRepository, not both."
}

$usesMountedRepository = -not [string]::IsNullOrWhiteSpace($MountedRepository)
$localArchive = $null
$remoteArchive = $null
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))

if (-not $usesMountedRepository) {
    if ([string]::IsNullOrWhiteSpace($LocalRepository)) {
        $LocalRepository = $repositoryRoot
    }
    else {
        $LocalRepository = (Resolve-Path -LiteralPath $LocalRepository).Path
    }

    if (-not (Test-Path -LiteralPath (
        Join-Path $LocalRepository "TOTP.UI.Avalonia.Desktop\TOTP.UI.Avalonia.Desktop.csproj") -PathType Leaf)) {
        throw "The local OTP Harbor repository was not found at '$LocalRepository'."
    }

    if (-not (Get-Command scp -ErrorAction SilentlyContinue)) {
        throw "OpenSSH secure copy (scp.exe) is required on the Windows host."
    }
    if (-not (Get-Command tar -ErrorAction SilentlyContinue)) {
        throw "A tar executable is required on the Windows host."
    }
}

$versionTag = & $GitExecutable -C $repositoryRoot describe --tags --abbrev=0 --match "v[0-9]*" 2>$null
if ($LASTEXITCODE -ne 0 -or $versionTag -notmatch '^v(?<version>\d+\.\d+\.\d+(?:-rc\d+)?)$') {
    throw "A semantic release tag is required to version the VM test build."
}
$productVersion = $Matches.version
$informationalVersion = "$productVersion+local"

if ([string]::IsNullOrWhiteSpace($VmHost)) {
    if (-not (Get-Command Get-VMNetworkAdapter -ErrorAction SilentlyContinue)) {
        throw "The Hyper-V PowerShell module is unavailable. Supply the VM address with -VmHost."
    }

    try {
        $vmAdapters = @(Get-VMNetworkAdapter -VMName $VmName -ErrorAction Stop |
            Sort-Object @{ Expression = {
                if ($_.Name -eq $PreferredNetworkAdapter) { 0 } else { 1 }
            } })
        $candidateAddresses = $vmAdapters | Select-Object -ExpandProperty IPAddresses
    }
    catch {
        throw "Could not inspect Hyper-V VM '$VmName'. Supply its IPv4 address with -VmHost. $($_.Exception.Message)"
    }

    $VmHost = $candidateAddresses |
        Where-Object {
            $parsedAddress = $null
            [Net.IPAddress]::TryParse($_, [ref]$parsedAddress) -and
                $parsedAddress.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetwork -and
                -not $parsedAddress.IsIPv6LinkLocal -and
                -not $_.StartsWith("127.", [StringComparison]::Ordinal) -and
                -not $_.StartsWith("169.254.", [StringComparison]::Ordinal)
        } |
        Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($VmHost)) {
        foreach ($vmAdapter in $vmAdapters) {
            $adapterMacAddress = $vmAdapter.MacAddress -replace "[:-]", ""
            $VmHost = Get-NetNeighbor -AddressFamily IPv4 -ErrorAction SilentlyContinue |
                Where-Object {
                    $neighborMac = $_.LinkLayerAddress -replace "[:-]", ""
                    $_.State -ne "Unreachable" -and
                        $neighborMac -eq $adapterMacAddress
                } |
                Select-Object -ExpandProperty IPAddress -First 1
            if (-not [string]::IsNullOrWhiteSpace($VmHost)) {
                break
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($VmHost)) {
        throw "No IPv4 address could be discovered for Hyper-V VM '$VmName'. Start and sign in to the VM, or supply its address with -VmHost."
    }

    Write-Host "Resolved Hyper-V VM '$VmName' to $VmHost."
}

$remoteScript = @'
set -Eeuo pipefail

source_root="$(printf '%s' "$1" | base64 --decode)"
source_mode="$(printf '%s' "$2" | base64 --decode)"
target_input="$(printf '%s' "$3" | base64 --decode)"
configuration="$(printf '%s' "$4" | base64 --decode)"
product_version="$(printf '%s' "$5" | base64 --decode)"
informational_version="$(printf '%s' "$6" | base64 --decode)"

case "$target_input" in
    "~") target_root="$HOME" ;;
    "~/"*) target_root="$HOME/${target_input:2}" ;;
    /*) target_root="$target_input" ;;
    *) printf 'VM repository must be an absolute path or start with ~/\n' >&2; exit 2 ;;
esac

target_root="$(realpath -m -- "$target_root")"

case "$target_root" in
    "$HOME"/source/*) ;;
    *) printf 'Refusing to synchronize outside %s/source/.\n' "$HOME" >&2; exit 2 ;;
esac

archive_path=''
staging_root=''
cleanup_sync_inputs() {
    if [[ -n "$staging_root" ]]; then
        rm -rf -- "$staging_root"
    fi
    if [[ -n "$archive_path" ]]; then
        rm -f -- "$archive_path"
    fi
}
trap cleanup_sync_inputs EXIT

case "$source_mode" in
    mounted)
        source_root="$(realpath -m -- "$source_root")"
        if [[ ! -f "$source_root/TOTP.UI.Avalonia.Desktop/TOTP.UI.Avalonia.Desktop.csproj" ]]; then
            printf 'The mounted repository was not found at %s\n' "$source_root" >&2
            exit 2
        fi
        ;;
    archive)
        case "$source_root" in
            /tmp/otp-harbor-sync-*.tar.gz) ;;
            *) printf 'Refusing to read an unexpected synchronization archive path.\n' >&2; exit 2 ;;
        esac
        if [[ ! -f "$source_root" ]]; then
            printf 'The synchronization archive was not found at %s\n' "$source_root" >&2
            exit 2
        fi
        archive_path="$source_root"
        mkdir -p -- "$HOME/source"
        staging_root="$(mktemp -d "$HOME/source/.otp-harbor-sync.XXXXXX")"
        tar -xzf "$source_root" -C "$staging_root"
        if [[ ! -f "$staging_root/TOTP.UI.Avalonia.Desktop/TOTP.UI.Avalonia.Desktop.csproj" ]]; then
            printf 'The synchronization archive did not contain the OTP Harbor repository.\n' >&2
            exit 2
        fi
        source_root="$staging_root"
        ;;
    *)
        printf 'Unknown synchronization source mode: %s\n' "$source_mode" >&2
        exit 2
        ;;
esac

printf 'Synchronizing %s to %s...\n' "$source_root" "$target_root"
mkdir -p -- "$target_root"
rsync -a --delete \
    --exclude='.git/' \
    --exclude='.vs/' \
    --exclude='bin/' \
    --exclude='obj/' \
    --exclude='artifacts/' \
    "$source_root/" "$target_root/"

if [[ "$source_mode" == 'archive' ]]; then
    rm -f -- "$archive_path"
    rm -rf -- "$staging_root"
    archive_path=''
    staging_root=''
    source_root=''
fi

cd -- "$target_root"

printf 'Restoring the Avalonia desktop project...\n'
dotnet restore \
    TOTP.UI.Avalonia.Desktop/TOTP.UI.Avalonia.Desktop.csproj \
    --configfile NuGet.config

user_id="$(id -u)"
desktop_session_pid=''
for desktop_process in Xwayland gnome-shell xfce4-session xfce4-panel; do
    desktop_session_pid="$(pgrep -u "$user_id" -o -x "$desktop_process" || true)"
    if [[ -n "$desktop_session_pid" ]]; then
        break
    fi
done
if [[ -n "$desktop_session_pid" && -r "/proc/$desktop_session_pid/environ" ]]; then
    while IFS= read -r -d '' session_entry; do
        case "$session_entry" in
            DISPLAY=*|WAYLAND_DISPLAY=*|XAUTHORITY=*|DBUS_SESSION_BUS_ADDRESS=*|\
            XDG_RUNTIME_DIR=*|XDG_SESSION_TYPE=*|XDG_CURRENT_DESKTOP=*|\
            XDG_CONFIG_HOME=*|XDG_DATA_HOME=*|XDG_STATE_HOME=*)
                export "$session_entry"
                ;;
        esac
    done < "/proc/$desktop_session_pid/environ"
fi

export DISPLAY="${DISPLAY:-:0}"
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/run/user/$user_id}"
if [[ -z "${DBUS_SESSION_BUS_ADDRESS:-}" && -S "$XDG_RUNTIME_DIR/bus" ]]; then
    export DBUS_SESSION_BUS_ADDRESS="unix:path=$XDG_RUNTIME_DIR/bus"
fi
if [[ -z "${XAUTHORITY:-}" && -f "$HOME/.Xauthority" ]]; then
    export XAUTHORITY="$HOME/.Xauthority"
fi

if [[ -z "${XAUTHORITY:-}" || ! -r "$XAUTHORITY" ]]; then
    xwayland_authority="$(
        find "$XDG_RUNTIME_DIR" -maxdepth 1 -type f \
            -name '.mutter-Xwaylandauth.*' -user "$user_id" \
            -printf '%T@ %p\n' 2>/dev/null |
            sort -nr |
            head -n 1 |
            cut -d ' ' -f 2-
    )"
    if [[ -n "$xwayland_authority" ]]; then
        export XAUTHORITY="$xwayland_authority"
    fi
fi

printf 'Starting OTP Harbor (%s) on display %s...\n' "$configuration" "$DISPLAY"
set +e
dotnet run \
    --project TOTP.UI.Avalonia.Desktop/TOTP.UI.Avalonia.Desktop.csproj \
    --configuration "$configuration" \
    --no-restore \
    -p:Version="$product_version" \
    -p:InformationalVersion="$informational_version" \
    -p:IncludeSourceRevisionInInformationalVersion=false
app_exit_code=$?
set -e

if (( app_exit_code != 0 )); then
    log_file="${XDG_STATE_HOME:-$HOME/.local/state}/totp-manager/logs/app.log"
    printf 'OTP Harbor exited with code %s.\n' "$app_exit_code" >&2
    if [[ -f "$log_file" ]]; then
        printf 'Recent redacted application log entries from %s:\n' "$log_file" >&2
        tail -n 25 -- "$log_file" >&2
    else
        printf 'No application log was found at %s.\n' "$log_file" >&2
    fi
fi
exit "$app_exit_code"
'@

$remoteScript = $remoteScript.Replace("`r`n", "`n").Replace("`r", "`n")
$encodedScript = [Convert]::ToBase64String(
    [Text.Encoding]::UTF8.GetBytes($remoteScript))
$encodedVmRepository = [Convert]::ToBase64String(
    [Text.Encoding]::UTF8.GetBytes($VmRepository))
$encodedConfiguration = [Convert]::ToBase64String(
    [Text.Encoding]::UTF8.GetBytes($Configuration))
$encodedProductVersion = [Convert]::ToBase64String(
    [Text.Encoding]::UTF8.GetBytes($productVersion))
$encodedInformationalVersion = [Convert]::ToBase64String(
    [Text.Encoding]::UTF8.GetBytes($informationalVersion))
$remoteCommand = "printf '%s' '$encodedScript' | base64 --decode | bash -s --"
$destination = "${VmUser}@${VmHost}"

try {
    if ($usesMountedRepository) {
        $sourceMode = "mounted"
        $sourceInput = $MountedRepository
    }
    else {
        $sourceMode = "archive"
        $archiveName = "otp-harbor-sync-$([Guid]::NewGuid().ToString('N')).tar.gz"
        $localArchive = Join-Path ([IO.Path]::GetTempPath()) $archiveName
        $remoteArchive = "/tmp/$archiveName"

        Write-Host "Packaging $LocalRepository..."
        Push-Location $LocalRepository
        try {
            & tar -czf $localArchive `
                --exclude='./.git' `
                --exclude='./.vs' `
                --exclude='*/bin' `
                --exclude='*/obj' `
                --exclude='./artifacts' `
                .
            if ($LASTEXITCODE -ne 0) {
                throw "Creating the repository synchronization archive failed with exit code $LASTEXITCODE."
            }
        }
        finally {
            Pop-Location
        }

        Write-Host "Uploading the working tree to $destination..."
        & scp @sshConnectionOptions $localArchive "${destination}:$remoteArchive"
        if ($LASTEXITCODE -ne 0) {
            throw "Uploading the repository synchronization archive failed with exit code $LASTEXITCODE."
        }
        $sourceInput = $remoteArchive
    }

    $encodedSourceInput = [Convert]::ToBase64String(
        [Text.Encoding]::UTF8.GetBytes($sourceInput))
    $encodedSourceMode = [Convert]::ToBase64String(
        [Text.Encoding]::UTF8.GetBytes($sourceMode))

    Write-Host "Connecting to $destination..."
    & ssh @sshConnectionOptions -t $destination $remoteCommand `
        $encodedSourceInput $encodedSourceMode $encodedVmRepository $encodedConfiguration `
        $encodedProductVersion $encodedInformationalVersion
    if ($LASTEXITCODE -ne 0) {
        if ($LASTEXITCODE -eq 255) {
            throw "SSH could not connect to $destination. In the VM, install and start OpenSSH with: sudo apt install openssh-server rsync && sudo systemctl enable --now ssh"
        }
        throw "The VM test command failed with exit code $LASTEXITCODE."
    }
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($localArchive) -and
        (Test-Path -LiteralPath $localArchive)) {
        Remove-Item -LiteralPath $localArchive -Force
    }
}
