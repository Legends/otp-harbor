#requires -Version 7.0
<#
.SYNOPSIS
Checks the no-argument NativeAOT installer using fake build/device tools only.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '../shared/Testing.Common.ps1')
$testRoot = Join-Path (Get-OtpHarborRepositoryRoot) ('artifacts/android/testing/native-phone-tests/' + [guid]::NewGuid().ToString('N') + '/source with spaces')
$testScripts = Join-Path $testRoot 'scripts/testing/android'
$testShared = Join-Path $testRoot 'scripts/testing/shared'
$testNdk = Join-Path $testRoot '.codex/tools/android-sdk/ndk/28.2.13676358'
foreach ($directory in @($testScripts, $testShared, $testNdk)) { $null = New-Item -ItemType Directory -Path $directory -Force }
[IO.File]::WriteAllText((Join-Path $testRoot 'OtpHarbor.sln'), '')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-AndroidNativeAotPhone.ps1') -Destination $testScripts
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../shared/Testing.Common.ps1') -Destination $testShared
$testInstaller = Join-Path $testScripts 'Install-AndroidNativeAotPhone.ps1'
$testAdb = Join-Path $testRoot 'adb fixture.ps1'
[IO.File]::WriteAllText($testAdb, @'
$fixture = $global:OtpHarborNativePhoneFixture
$global:LASTEXITCODE = 0
if ($args[0] -eq '-s') {
    if ($args[1] -ne 'synthetic-usb-phone') { throw 'Wrong device selected.' }
    $command = $args[2..($args.Length - 1)] -join ' '
} else { $command = $args -join ' ' }
$fixture.Calls.Add($command)
switch -Regex ($command) {
    '^-d get-serialno$' {
        switch ($fixture.Case) {
            'no-phone' { $global:LASTEXITCODE = 1; 'unknown' }
            'multiple-phones' { $global:LASTEXITCODE = 1; 'unknown' }
            'emulator' { 'emulator-5554' }
            default { 'synthetic-usb-phone' }
        }; break
    }
    '^get-state$' {
        if ($fixture.Case -eq 'unauthorized') { $global:LASTEXITCODE = 1; 'unauthorized' } else { 'device' }; break
    }
    '^install -r ' {
        if ($fixture.Case -eq 'install-failed') { $global:LASTEXITCODE = 1 } else { 'Success' }; break
    }
    '^shell cmd package resolve-activity ' {
        if ($fixture.Case -eq 'wrong-launcher') { 'io.github.legends.otpharbor/OtherActivity' }
        else { 'io.github.legends.otpharbor.nativeaotprobe/SyntheticActivity' }; break
    }
    '^shell am start ' {
        if ($fixture.Case -eq 'launch-failed') { $global:LASTEXITCODE = 1; 'Status: error' }
        elseif ($fixture.Case -eq 'launch-error-status') { 'Status: error' }
        else { 'Status: ok' }; break
    }
    '^shell pidof ' {
        $fixture.ProcessQueries++
        if ($fixture.Case -eq 'crashes-after-launch') { $global:LASTEXITCODE = 1 }
        elseif ($fixture.Case -eq 'restarts-after-launch' -and $fixture.ProcessQueries -gt 1) { '45678' }
        else { '12345' }; break
    }
    default { throw "Unexpected device command: $command" }
}
'@)
[IO.File]::WriteAllText((Join-Path $testScripts 'Build-AndroidAotExperiment.ps1'), @'
param([string]$Runtime, [string]$Architecture, [string]$NdkDirectory)
$fixture = $global:OtpHarborNativePhoneFixture
$fixture.Calls.Add('build-current-native-source')
if ($Runtime -ne 'NativeAOT' -or $Architecture -ne 'universal' -or
    $NdkDirectory -ne $fixture.Ndk -or (Get-Location).Path -ne $fixture.Root) { throw 'Incorrect build settings or directory.' }
if ($fixture.Case -eq 'build-failed') { throw 'Synthetic build failure.' }
$apk = Join-Path $fixture.Root 'artifacts/android/testing/aot/NativeAOT/universal/OTP-Harbor-AOT-Test-NativeAOT.apk'
if ($fixture.Case -eq 'missing-apk') {
    if (Test-Path -LiteralPath $apk) { Remove-Item -LiteralPath $apk -Force }
    return
}
$null = New-Item -ItemType Directory -Path (Split-Path $apk) -Force
[IO.File]::WriteAllText($apk, 'Synthetic APK placeholder; never installed on a device.')
'@)
function Get-Command {
    param([string]$Name, $ErrorAction)
    if ($Name -ne 'adb') { throw "Unexpected tool lookup: $Name" }
    [pscustomobject]@{ Source = $testAdb }
}
function Start-Sleep { param([int]$Seconds) }

$cases = @(
    @{ Name = 'normal'; Builds = 1; Installs = 1; Launches = 1; Error = $null },
    @{ Name = 'no-phone'; Builds = 0; Installs = 0; Launches = 0; Error = 'Connect exactly one physical USB phone' },
    @{ Name = 'multiple-phones'; Builds = 0; Installs = 0; Launches = 0; Error = 'Connect exactly one physical USB phone' },
    @{ Name = 'emulator'; Builds = 0; Installs = 0; Launches = 0; Error = 'Connect exactly one physical USB phone' },
    @{ Name = 'unauthorized'; Builds = 0; Installs = 0; Launches = 0; Error = 'offline or unauthorized' },
    @{ Name = 'missing-ndk'; Builds = 0; Installs = 0; Launches = 0; Error = 'NDK 28.2.13676358 was not found' },
    @{ Name = 'build-failed'; Builds = 1; Installs = 0; Launches = 0; Error = 'Synthetic build failure' },
    @{ Name = 'missing-apk'; Builds = 1; Installs = 0; Launches = 0; Error = 'did not produce its APK' },
    @{ Name = 'install-failed'; Builds = 1; Installs = 1; Launches = 0; Error = 'Installation failed' },
    @{ Name = 'wrong-launcher'; Builds = 1; Installs = 1; Launches = 0; Error = 'not uniquely identified' },
    @{ Name = 'launch-failed'; Builds = 1; Installs = 1; Launches = 1; Error = 'launch failed' },
    @{ Name = 'launch-error-status'; Builds = 1; Installs = 1; Launches = 1; Error = 'launch failed' },
    @{ Name = 'crashes-after-launch'; Builds = 1; Installs = 1; Launches = 1; Error = 'exited during startup' },
    @{ Name = 'restarts-after-launch'; Builds = 1; Installs = 1; Launches = 1; Error = 'restarted during startup' }
)
$savedNdk = $env:ANDROID_NDK_HOME
$savedSdk = $env:ANDROID_HOME
$savedSdkRoot = $env:ANDROID_SDK_ROOT
$savedLocal = $env:LOCALAPPDATA
$savedProgramFiles = ${env:ProgramFiles(x86)}
Push-Location ([IO.Path]::GetTempPath())
try {
    $env:ANDROID_NDK_HOME = $null
    $env:ANDROID_HOME = $null
    $env:ANDROID_SDK_ROOT = $null
    $env:LOCALAPPDATA = $null
    ${env:ProgramFiles(x86)} = $null
    foreach ($case in $cases) {
        $global:OtpHarborNativePhoneFixture = @{ Case = $case.Name; Root = $testRoot; Ndk = $testNdk; ProcessQueries = 0; Calls = [Collections.Generic.List[string]]::new() }
        $revision = if ($case.Name -eq 'missing-ndk') { '27.0.0' } else { '28.2.13676358' }
        [IO.File]::WriteAllText((Join-Path $testNdk 'source.properties'), "Pkg.Revision = $revision")
        $failure = $null
        try { & $testInstaller | Out-Null } catch { $failure = $_.Exception.Message }
        if (($null -eq $case.Error -and $null -ne $failure) -or
            ($null -ne $case.Error -and ($null -eq $failure -or $failure -notlike "*$($case.Error)*"))) { throw "Case $($case.Name) failed: $failure" }
        $calls = $global:OtpHarborNativePhoneFixture.Calls
        if (@($calls | Where-Object { $_ -eq 'build-current-native-source' }).Count -ne $case.Builds -or
            @($calls | Where-Object { $_ -like 'install -r *' }).Count -ne $case.Installs -or
            @($calls | Where-Object { $_ -like 'shell am start *' }).Count -ne $case.Launches) { throw "Unexpected deployment steps: $($case.Name)" }
        if ($calls -match 'uninstall|\bclear\b|force-stop') { throw 'The installer attempted an unexpected destructive or restart operation.' }
        if ((Get-Location).Path -ne ([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)) { throw 'The installer changed the caller directory.' }
        Write-Output "PASS: $($case.Name)"
    }
}
finally {
    Pop-Location
    $env:ANDROID_NDK_HOME = $savedNdk
    $env:ANDROID_HOME = $savedSdk
    $env:ANDROID_SDK_ROOT = $savedSdkRoot
    $env:LOCALAPPDATA = $savedLocal
    ${env:ProgramFiles(x86)} = $savedProgramFiles
    Remove-Variable -Name OtpHarborNativePhoneFixture -Scope Global -ErrorAction SilentlyContinue
}
Write-Output "PASS: $($cases.Count) NativeAOT installer scenarios; no real build tools or devices accessed."
$global:LASTEXITCODE = 0
