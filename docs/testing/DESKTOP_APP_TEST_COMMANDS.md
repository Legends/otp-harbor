# Desktop application test commands

Use the repository root as the working directory unless stated otherwise.

## Windows host — Avalonia desktop application

Run in **Windows PowerShell** from the repository root:

```powershell
.\scripts\dev\Publish-And-Run-AvaloniaWindows.ps1 -Configuration Release -StopRunningInstance
```

This publishes and starts the Windows version of `TOTP.UI.Avalonia.Desktop`.

## Ubuntu Hyper-V VM — full Linux desktop test

Run in **Windows PowerShell** from the repository root while the Ubuntu VM is running and its desktop user is signed in:

```powershell
.\scripts\testing\Sync-Restore-Run-AvaloniaLinuxVm.ps1
```

This packages the current Windows working tree, transfers it over SSH to the
VM-local repository, restores the Avalonia project, and starts it in the Ubuntu
desktop session. Build output, IDE state, Git metadata, and `artifacts` are
excluded from the transfer. The temporary archive is removed after
synchronization. The runner discovers the active GNOME/Wayland, Xwayland, or
XFCE session and forwards its display, authorization, D-Bus, and runtime
environment to the application. It derives the product version from the latest
reachable semantic Git tag and adds `+local` to the informational version. The
support report therefore identifies the release baseline without pretending
that the working-tree build is a published package. Git is discovered from
`PATH`, a standalone per-user or machine installation, or a Visual Studio
installation. Use `-GitExecutable` only when Git is installed elsewhere.

The default VM-local repository is `~/source/otp-harbor`. A pre-existing
shared mount can still be used explicitly when needed:

```powershell
.\scripts\testing\Sync-Restore-Run-AvaloniaLinuxVm.ps1 `
    -MountedRepository /mnt/otp-harbor
```

The VM requires `openssh-server`, `rsync`, `tar`, and the .NET 10 SDK. The
runner automatically uses `%USERPROFILE%\.ssh\legends` when that key exists.
Use another identity explicitly when needed:

```powershell
.\scripts\testing\Sync-Restore-Run-AvaloniaLinuxVm.ps1 `
    -SshIdentityFile "$env:USERPROFILE\.ssh\id_ed25519"
```

With an identity file, SSH runs in batch mode: upload and execution do not
prompt for a password, and a missing or rejected key fails immediately.

The VM's persistent address and outbound NAT configuration are documented in
[`UBUNTU_HYPERV_NETWORK.md`](UBUNTU_HYPERV_NETWORK.md). Use the repair helpers there instead of
depending on the changing Hyper-V Default Switch address.

## Ubuntu WSL2/WSLg — fast Linux development test

Run in the **Ubuntu WSL shell**:

```bash
./scripts/testing/run-avalonia-wsl.sh
```

Use WSL/WSLg for quick development checks. Use the full Hyper-V VM for Linux desktop and end-user acceptance testing.
