Set-StrictMode -Version Latest

function Assert-UpgradeRegularPath {
    param([Parameter(Mandatory)][string]$Path)
    $candidate = [IO.Path]::GetFullPath($Path)
    while (-not [string]::IsNullOrWhiteSpace($candidate)) {
        if (Test-Path -LiteralPath $candidate) {
            if (((Get-Item -LiteralPath $candidate -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Test paths must not contain junctions or symbolic links.'
            }
        }
        $candidate = [IO.Path]::GetDirectoryName($candidate)
    }
}

function Get-UpgradeDataDirectories {
    param(
        [string]$RoamingRoot = [Environment]::GetFolderPath('ApplicationData'),
        [string]$LocalRoot = [Environment]::GetFolderPath('LocalApplicationData'),
        [string]$PackageFamilyName
    )
    $roots = @($RoamingRoot)
    $packagesRoot = Join-Path $LocalRoot 'Packages'
    if ($PackageFamilyName) {
        $roots += Join-Path (Join-Path $packagesRoot $PackageFamilyName) 'LocalCache/Roaming'
    } elseif (Test-Path -LiteralPath $packagesRoot -PathType Container) {
        # Include both raw and MSIX-virtualized locations. Never search unrelated packages.
        foreach ($package in Get-ChildItem -LiteralPath $packagesRoot -Directory -Filter 'Legends77.OTPHarbor_*') {
            $roots += Join-Path $package.FullName 'LocalCache/Roaming'
        }
    }
    foreach ($root in $roots) {
        foreach ($name in @('TOTP-Manager', 'OTP-Harbor')) {
            Join-Path $root $name
        }
    }
}

function Assert-UpgradeCleanProfile {
    param([Parameter(Mandatory)][string[]]$Directories)
    $existing = @()
    foreach ($directory in $Directories) {
        Assert-UpgradeRegularPath $directory
        if (Test-Path -LiteralPath $directory) {
            $existing += [IO.Path]::GetFullPath($directory)
        }
    }
    if ($existing.Count -gt 0) {
        throw ("Existing OTP Harbor data detected at:`n - " + ($existing -join "`n - ") +
            "`nThis includes legacy TOTP-Manager and Store-virtualized data. Use a clean test profile; no data was changed.")
    }
}

function Get-UpgradeVaultDirectory {
    param([Parameter(Mandatory)][string[]]$Directories, [Parameter(Mandatory)][string]$FileName)
    $matches = @($Directories | Where-Object { Test-Path -LiteralPath (Join-Path $_ $FileName) -PathType Leaf })
    if ($matches.Count -ne 1) { throw 'Expected exactly one test vault. Resolve duplicate or missing test data manually.' }
    Assert-UpgradeRegularPath $matches[0]
    return $matches[0]
}

function Assert-UpgradeAppClosed {
    if (Get-Process -Name 'TOTP.UI.Avalonia.Desktop', 'TOTP-Manager', 'OTP-Harbor' -ErrorAction SilentlyContinue) {
        throw 'Close OTP Harbor before capturing or verifying the test vault.'
    }
}

function Get-UpgradeStorePackage {
    $packages = @(Get-AppxPackage -Name 'Legends77.OTPHarbor')
    if ($packages.Count -ne 1) { throw 'Install exactly one OTP Harbor Store package for this test user.' }
    $package = $packages[0]
    if ($package.Publisher -cne 'CN=84095A7C-6458-436E-ABF2-DC02311E25F9' -or
        $package.SignatureKind.ToString() -ne 'Store' -or $package.IsDevelopmentMode) {
        throw 'This acceptance test requires the genuine Store-signed OTP Harbor package.'
    }
    return $package
}

function Get-UpgradeHash {
    param([Parameter(Mandatory)][string]$Path)
    Assert-UpgradeRegularPath $Path
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $algorithm = $null
    try {
        $algorithm = [Security.Cryptography.SHA256]::Create()
        return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '')
    } finally {
        if ($null -ne $algorithm) { $algorithm.Dispose() }
        $stream.Dispose()
    }
}

function Save-UpgradeState {
    param([Parameter(Mandatory)]$State, [Parameter(Mandatory)][string]$Path)
    $temporary = "$Path.$([Guid]::NewGuid().ToString('N')).tmp"
    $State | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $temporary -Encoding utf8
    Move-Item -LiteralPath $temporary -Destination $Path -Force
}

function Protect-UpgradeKitDirectory {
    param([Parameter(Mandatory)][string]$Path)
    Assert-UpgradeRegularPath $Path
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    $acl.SetOwner($sid)
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
        $sid, 'FullControl', 'ContainerInherit, ObjectInherit', 'None', 'Allow'))
    $directory = [IO.DirectoryInfo]::new($Path)
    if ($PSVersionTable.PSVersion.Major -lt 6) {
        $directory.SetAccessControl($acl)
    } else {
        [IO.FileSystemAclExtensions]::SetAccessControl($directory, $acl)
    }
}

function Move-UpgradeDataAside {
    param([Parameter(Mandatory)][string]$Directory)
    $source = [IO.Path]::GetFullPath($Directory)
    Assert-UpgradeRegularPath $source
    if (Test-Path -LiteralPath $source -PathType Leaf) { throw 'An application-data directory is occupied by a file.' }
    if (-not (Test-Path -LiteralPath $source -PathType Container)) { return }
    $destination = "$source.before-synthetic-v2-$([Guid]::NewGuid().ToString('N'))"
    if ([IO.Path]::GetDirectoryName($destination) -cne [IO.Path]::GetDirectoryName($source)) {
        throw 'Preserved application data must remain beside its original directory.'
    }
    Assert-UpgradeRegularPath $destination
    if (Test-Path -LiteralPath $destination) { throw 'The preservation destination already exists.' }
    [IO.Directory]::Move($source, $destination)
    [pscustomobject]@{ Original = $source; Preserved = $destination }
}
