Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:DevelopmentSnapshotRoot = Join-Path $env:LOCALAPPDATA 'OTP-Harbor\DevelopmentAccountSnapshots'
$script:DesktopVaultRoot = Join-Path $env:APPDATA 'OTP-Harbor'
$script:VaultFileName = 'master.vault'
$script:EnvelopeFileName = 'authorization-envelope.bin'
$script:AndroidPackageName = 'io.github.legends.otpharbor.debug'
$script:AndroidVaultRoot = 'files/TOTP-Manager'

function Assert-WindowsDevelopmentHost {
    if (-not $IsWindows) { throw 'These development snapshot scripts require Windows.' }
}

function Protect-DevelopmentSnapshotDirectory {
    param([Parameter(Mandatory)][string]$Path)

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    & icacls.exe $Path '/inheritance:r' "/grant:r" "${identity}:(OI)(CI)F" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not restrict snapshot directory '$Path' to the current user." }
}

function Get-Sha256 {
    param([Parameter(Mandatory)][string]$Path)
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function New-EncryptedSnapshot {
    param(
        [Parameter(Mandatory)][string]$VaultPath,
        [Parameter(Mandatory)][string]$EnvelopePath,
        [Parameter(Mandatory)][ValidateSet('desktop-source', 'desktop-recovery', 'android-recovery')][string]$Kind,
        [switch]$SetAsDesktopSource
    )

    foreach ($path in @($VaultPath, $EnvelopePath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required encrypted state file was not found: $path"
        }
    }

    Protect-DevelopmentSnapshotDirectory -Path $script:DevelopmentSnapshotRoot
    $snapshotName = '{0}-{1}-{2}' -f (Get-Date -Format 'yyyyMMdd-HHmmssfff'), $Kind, ([guid]::NewGuid().ToString('N'))
    $snapshotPath = Join-Path $script:DevelopmentSnapshotRoot $snapshotName
    Protect-DevelopmentSnapshotDirectory -Path $snapshotPath

    $sourceVaultHashBefore = Get-Sha256 -Path $VaultPath
    $sourceEnvelopeHashBefore = Get-Sha256 -Path $EnvelopePath
    $snapshotVaultPath = Join-Path $snapshotPath $script:VaultFileName
    $snapshotEnvelopePath = Join-Path $snapshotPath $script:EnvelopeFileName
    Copy-Item -LiteralPath $VaultPath -Destination $snapshotVaultPath
    Copy-Item -LiteralPath $EnvelopePath -Destination $snapshotEnvelopePath

    $sourceVaultHashAfter = Get-Sha256 -Path $VaultPath
    $sourceEnvelopeHashAfter = Get-Sha256 -Path $EnvelopePath
    if ($sourceVaultHashBefore -ne $sourceVaultHashAfter -or
        $sourceEnvelopeHashBefore -ne $sourceEnvelopeHashAfter -or
        $sourceVaultHashAfter -ne (Get-Sha256 -Path $snapshotVaultPath) -or
        $sourceEnvelopeHashAfter -ne (Get-Sha256 -Path $snapshotEnvelopePath)) {
        throw 'Encrypted application state changed while it was being snapshotted. No snapshot will be used.'
    }

    $manifest = [ordered]@{
        schemaVersion = 1
        kind = $Kind
        createdUtc = [DateTimeOffset]::UtcNow.ToString('O')
        vault = [ordered]@{ file = $script:VaultFileName; sha256 = $sourceVaultHashAfter }
        authorizationEnvelope = [ordered]@{ file = $script:EnvelopeFileName; sha256 = $sourceEnvelopeHashAfter }
    }
    $manifestPath = Join-Path $snapshotPath 'manifest.json'
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM

    if ($SetAsDesktopSource) {
        $pointerPath = Join-Path $script:DevelopmentSnapshotRoot 'latest-desktop-source.txt'
        [IO.File]::WriteAllText($pointerPath, $snapshotPath, [Text.UTF8Encoding]::new($false))
    }

    return $snapshotPath
}

function Get-ValidatedSnapshot {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        $pointerPath = Join-Path $script:DevelopmentSnapshotRoot 'latest-desktop-source.txt'
        if (-not (Test-Path -LiteralPath $pointerPath -PathType Leaf)) {
            throw 'No desktop source snapshot exists. Run Clear-DesktopDevelopmentAccounts.ps1 first.'
        }
        $Path = ([IO.File]::ReadAllText($pointerPath)).Trim()
    }

    $resolved = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
    $manifestPath = Join-Path $resolved 'manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'Snapshot manifest is missing.' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.kind -ne 'desktop-source') {
        throw 'Only a validated desktop-source snapshot can fill development apps.'
    }

    $vaultPath = Join-Path $resolved ([string]$manifest.vault.file)
    $envelopePath = Join-Path $resolved ([string]$manifest.authorizationEnvelope.file)
    if ((Get-Sha256 -Path $vaultPath) -ne [string]$manifest.vault.sha256 -or
        (Get-Sha256 -Path $envelopePath) -ne [string]$manifest.authorizationEnvelope.sha256) {
        throw 'Snapshot hash validation failed.'
    }

    [pscustomobject]@{
        Path = $resolved
        VaultPath = $vaultPath
        EnvelopePath = $envelopePath
        VaultHash = [string]$manifest.vault.sha256
        EnvelopeHash = [string]$manifest.authorizationEnvelope.sha256
    }
}

function Get-AdbPath {
    $command = Get-Command adb -ErrorAction SilentlyContinue
    if ($null -ne $command) { return $command.Source }
    foreach ($root in @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, (Join-Path $env:LOCALAPPDATA 'Android\Sdk'))) {
        if ([string]::IsNullOrWhiteSpace($root)) { continue }
        $candidate = Join-Path $root 'platform-tools\adb.exe'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    throw 'ADB was not found. Install Android platform-tools or add adb.exe to PATH.'
}

function Resolve-AndroidDevice {
    param([string]$Device)

    $adb = Get-AdbPath
    if (-not [string]::IsNullOrWhiteSpace($Device) -and $Device.Contains(':')) {
        & $adb connect $Device | Out-Null
    }
    if (-not [string]::IsNullOrWhiteSpace($Device)) {
        $state = (& $adb -s $Device get-state 2>&1 | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or $state -ne 'device') {
            throw "Android device '$Device' is not connected and authorized (state: '$state')."
        }
        return [pscustomobject]@{ Adb = $adb; Device = $Device }
    }

    $devices = @(& $adb devices | Select-Object -Skip 1 | ForEach-Object {
        if ($_ -match '^(\S+)\s+device$') { $Matches[1] }
    })
    if ($devices.Count -ne 1) {
        throw 'Exactly one authorized Android device is required. Pass -Device "IP:Port" when needed.'
    }
    [pscustomobject]@{ Adb = $adb; Device = $devices[0] }
}

function Assert-AndroidDebugPackage {
    param([Parameter(Mandatory)]$Connection)
    & $Connection.Adb -s $Connection.Device shell pm path $script:AndroidPackageName 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'The OTP Harbor Android Debug package is not installed.' }
    & $Connection.Adb -s $Connection.Device shell run-as $script:AndroidPackageName id 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'The installed Android package is not a debuggable OTP Harbor build.' }
}

function Copy-AndroidPrivateFileToHost {
    param(
        [Parameter(Mandatory)]$Connection,
        [Parameter(Mandatory)][string]$RelativePath,
        [Parameter(Mandatory)][string]$Destination
    )

    $start = [Diagnostics.ProcessStartInfo]::new($Connection.Adb)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('-s', $Connection.Device, 'exec-out', 'run-as', $script:AndroidPackageName, 'cat', $RelativePath)) {
        $start.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($start)
    $errorTask = $process.StandardError.ReadToEndAsync()
    try {
        $output = [IO.File]::Create($Destination)
        try { $process.StandardOutput.BaseStream.CopyTo($output) } finally { $output.Dispose() }
        $process.WaitForExit()
        $errorText = $errorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "Could not read Android encrypted state: $errorText" }
    }
    finally {
        $process.Dispose()
    }
}

function New-AndroidEncryptedSnapshot {
    param(
        [Parameter(Mandatory)]$Connection,
        [Parameter(Mandatory)][ValidateSet('android-recovery')][string]$Kind
    )

    $temporary = Join-Path ([IO.Path]::GetTempPath()) ('otp-harbor-android-state-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temporary | Out-Null
    $vault = Join-Path $temporary $script:VaultFileName
    $envelope = Join-Path $temporary $script:EnvelopeFileName
    try {
        Copy-AndroidPrivateFileToHost -Connection $Connection -RelativePath "$script:AndroidVaultRoot/$script:VaultFileName" -Destination $vault
        Copy-AndroidPrivateFileToHost -Connection $Connection -RelativePath "$script:AndroidVaultRoot/$script:EnvelopeFileName" -Destination $envelope
        New-EncryptedSnapshot -VaultPath $vault -EnvelopePath $envelope -Kind $Kind
    }
    finally {
        if (Test-Path -LiteralPath $vault -PathType Leaf) { Remove-Item -LiteralPath $vault -Force }
        if (Test-Path -LiteralPath $envelope -PathType Leaf) { Remove-Item -LiteralPath $envelope -Force }
        if (Test-Path -LiteralPath $temporary -PathType Container) { Remove-Item -LiteralPath $temporary -Force }
    }
}

function Install-SnapshotOnAndroid {
    param([Parameter(Mandatory)]$Connection, [Parameter(Mandatory)]$Snapshot)

    $suffix = [guid]::NewGuid().ToString('N')
    $remoteVault = "/data/local/tmp/otp-harbor-vault-$suffix"
    $remoteEnvelope = "/data/local/tmp/otp-harbor-envelope-$suffix"
    if ($remoteVault -notmatch '^/data/local/tmp/otp-harbor-vault-[0-9a-f]{32}$' -or
        $remoteEnvelope -notmatch '^/data/local/tmp/otp-harbor-envelope-[0-9a-f]{32}$') {
        throw 'Refusing an invalid Android staging path.'
    }

    try {
        & $Connection.Adb -s $Connection.Device push $Snapshot.VaultPath $remoteVault | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not stage the encrypted vault on Android.' }
        & $Connection.Adb -s $Connection.Device push $Snapshot.EnvelopePath $remoteEnvelope | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not stage the authorization envelope on Android.' }
        & $Connection.Adb -s $Connection.Device shell run-as $script:AndroidPackageName mkdir -p $script:AndroidVaultRoot | Out-Null
        & $Connection.Adb -s $Connection.Device shell run-as $script:AndroidPackageName cp $remoteVault "$script:AndroidVaultRoot/$script:VaultFileName" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not install the encrypted vault in Android private storage.' }
        & $Connection.Adb -s $Connection.Device shell run-as $script:AndroidPackageName cp $remoteEnvelope "$script:AndroidVaultRoot/$script:EnvelopeFileName" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not install the authorization envelope in Android private storage.' }
        & $Connection.Adb -s $Connection.Device shell run-as $script:AndroidPackageName chmod 600 "$script:AndroidVaultRoot/$script:VaultFileName" "$script:AndroidVaultRoot/$script:EnvelopeFileName" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not restrict the restored Android files.' }
    }
    finally {
        & $Connection.Adb -s $Connection.Device shell rm -f $remoteVault $remoteEnvelope | Out-Null
    }

    $verificationRoot = Join-Path ([IO.Path]::GetTempPath()) ('otp-harbor-android-verify-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $verificationRoot | Out-Null
    $verificationVault = Join-Path $verificationRoot $script:VaultFileName
    $verificationEnvelope = Join-Path $verificationRoot $script:EnvelopeFileName
    try {
        Copy-AndroidPrivateFileToHost -Connection $Connection -RelativePath "$script:AndroidVaultRoot/$script:VaultFileName" -Destination $verificationVault
        Copy-AndroidPrivateFileToHost -Connection $Connection -RelativePath "$script:AndroidVaultRoot/$script:EnvelopeFileName" -Destination $verificationEnvelope
        if ((Get-Sha256 -Path $verificationVault) -ne $Snapshot.VaultHash -or
            (Get-Sha256 -Path $verificationEnvelope) -ne $Snapshot.EnvelopeHash) {
            throw 'Android restore verification failed.'
        }
    }
    finally {
        if (Test-Path -LiteralPath $verificationVault -PathType Leaf) { Remove-Item -LiteralPath $verificationVault -Force }
        if (Test-Path -LiteralPath $verificationEnvelope -PathType Leaf) { Remove-Item -LiteralPath $verificationEnvelope -Force }
        if (Test-Path -LiteralPath $verificationRoot -PathType Container) { Remove-Item -LiteralPath $verificationRoot -Force }
    }
}

Assert-WindowsDevelopmentHost
