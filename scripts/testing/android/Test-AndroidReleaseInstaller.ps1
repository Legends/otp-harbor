#requires -Version 5.1
<#
.SYNOPSIS
Tests Release Test installer behavior with fake build, signing and device tools.
.DESCRIPTION
No real dotnet or adb command is executed. Tiny synthetic APK fixtures are kept under
artifacts/android/testing/release-installer-tests for inspection; no device data is accessed.
#>
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
. (Join-Path $PSScriptRoot '../shared/Testing.Common.ps1')
$installerTestRoot = Join-Path (Get-OtpHarborRepositoryRoot) ('artifacts/android/testing/release-installer-tests/' + [guid]::NewGuid().ToString('N'))
$installerTools = Join-Path $installerTestRoot 'tools with spaces'
$null = New-Item -ItemType Directory -Path (Join-Path $installerTools 'lib') -Force
[IO.File]::WriteAllText((Join-Path $installerTools 'lib/apksigner.jar'), 'Synthetic signer fixture')
$installerAapt = Join-Path $installerTools 'aapt.ps1'
[IO.File]::WriteAllText($installerAapt, @'
$fixture = $global:OtpHarborReleaseInstallerFixture
$fixture.Calls.Add('inspect-apk')
$global:LASTEXITCODE = 0
$package = if ($fixture.Scenario -eq 'wrong-identity') { 'io.github.legends.otpharbor' } else { 'io.github.legends.otpharbor.releasetest' }
"package: name='$package' versionCode='3' versionName='0.1.0-release-test'"
"launchable-activity: name='crc6461813aeb5a554e26.MainActivity' label='OTP Harbor Release Test' icon='res/icon.png'"
if ($fixture.Scenario -eq 'debuggable') { 'application-debuggable' }
if ($fixture.Scenario -eq 'wrong-abi') { "native-code: 'x86_64'" } else { "native-code: 'arm64-v8a'" }
'@)
$installerJava = Join-Path $installerTools 'java.ps1'
[IO.File]::WriteAllText($installerJava, @'
$global:OtpHarborReleaseInstallerFixture.Calls.Add('verify-signature')
$global:LASTEXITCODE = if ($global:OtpHarborReleaseInstallerFixture.Scenario -eq 'bad-signature') { 1 } else { 0 }
'@)
$installerAdb = Join-Path $installerTools 'adb.ps1'
[IO.File]::WriteAllText($installerAdb, @'
$fixture = $global:OtpHarborReleaseInstallerFixture
$command = if ($args.Count -ge 3 -and $args[0] -eq '-s') {
    $fixture.Targets.Add([string]$args[1])
    $args[2..($args.Length - 1)] -join ' '
} else { $args -join ' ' }
$fixture.Calls.Add($command)
$global:LASTEXITCODE = 0
switch -Regex ($command) {
    '^-d get-serialno$' {
        switch ($fixture.Scenario) {
            'auto-multiple-phones' { $global:LASTEXITCODE = 1; 'unknown' }
            'auto-no-phone' { $global:LASTEXITCODE = 1; 'unknown' }
            'auto-emulator-only' { $global:LASTEXITCODE = 1; 'unknown' }
            'auto-adb-failed' { $global:LASTEXITCODE = 1 }
            'auto-invalid-emulator' { ' emulator-5554 ' }
            default { 'synthetic-physical-phone' }
        }
        break
    }
    '^get-state$' {
        switch ($fixture.Scenario) {
            'auto-offline' { 'offline' }
            'auto-unauthorized' { $global:LASTEXITCODE = 1; 'unauthorized' }
            default { 'device' }
        }
        break
    }
    '^install -r ' {
        if ($fixture.Scenario -eq 'install-failed') { $global:LASTEXITCODE = 1 } else { 'Success' }
        break
    }
    '^shell cmd package resolve-activity ' { 'io.github.legends.otpharbor.releasetest/crc6461813aeb5a554e26.MainActivity'; break }
    '^shell am start ' { 'Status: ok'; break }
    '^shell pidof ' { '12345'; break }
    '^shell dumpsys activity activities' { 'mResumedActivity: ActivityRecord{42 u0 io.github.legends.otpharbor.releasetest/crc6461813aeb5a554e26.MainActivity t1}'; break }
    '^shell dumpsys package ' { 'versionName=0.1.0-release-test'; 'versionCode=3 minSdk=28 targetSdk=36'; 'flags=[ HAS_CODE ]'; break }
    default { throw "Unexpected synthetic adb command: $command" }
}
'@)

function dotnet {
    $fixture = $global:OtpHarborReleaseInstallerFixture
    $global:LASTEXITCODE = 0
    if ($args[0] -eq '--version') { '10.0.404'; return }
    if ($args[0] -ne 'publish') { throw 'Unexpected build command.' }
    $fixture.Calls.Add('build-current-source')
    foreach ($required in @('-p:RunAOTCompilation=true', '-p:Optimize=true', '-p:ApplicationId=io.github.legends.otpharbor.releasetest')) {
        if ($args -notcontains $required) { throw "Missing required Release setting: $required" }
    }
    if ($fixture.Scenario -eq 'build-failed') { $global:LASTEXITCODE = 1; return }
    $outputIndex = [Array]::IndexOf($args, '-o')
    $outputDirectory = $args[$outputIndex + 1]
    $null = New-Item -ItemType Directory -Path $outputDirectory -Force
    $apk = Join-Path $outputDirectory 'io.github.legends.otpharbor.releasetest-Signed.apk'
    $archive = [IO.Compression.ZipFile]::Open($apk, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $entry = if ($fixture.Scenario -eq 'missing-aot') { 'synthetic.txt' }
            else { 'lib/arm64-v8a/libaot-TOTP.UI.Avalonia.Mobile.dll.so' }
        $null = $archive.CreateEntry($entry)
    }
    finally { $archive.Dispose() }
}

# The startup checker runs normally against fake adb; avoid its real observation delays.
function Start-Sleep { param([int]$Seconds) }

$installerCases = @(
    @{ Name = 'normal'; Options = @{}; Installs = 1; Launches = 1; Cold = 0; Error = $null },
    @{ Name = 'startup'; Options = @{ TestStartup = $true }; Installs = 1; Launches = 3; Cold = 3; Error = $null },
    @{ Name = 'build-only'; Options = @{ BuildOnly = $true }; Installs = 0; Launches = 0; Cold = 0; Error = $null },
    @{ Name = 'build-only-no-device'; Options = @{ BuildOnly = $true; Device = $null }; Installs = 0; Launches = 0; Cold = 0; Error = $null },
    @{ Name = 'auto-physical-phone'; Options = @{ Device = $null }; Installs = 1; Launches = 1; Cold = 0; Error = $null },
    @{ Name = 'auto-physical-and-emulator'; Options = @{ Device = $null }; Installs = 1; Launches = 1; Cold = 0; Error = $null },
    @{ Name = 'auto-startup'; Options = @{ Device = $null; TestStartup = $true }; Installs = 1; Launches = 3; Cold = 3; Error = $null },
    @{ Name = 'auto-multiple-phones'; Options = @{ Device = $null }; Builds = 0; Installs = 0; Launches = 0; Cold = 0; Error = 'Could not select one physical USB phone' },
    @{ Name = 'auto-no-phone'; Options = @{ Device = $null }; Builds = 0; Installs = 0; Launches = 0; Cold = 0; Error = 'Could not select one physical USB phone' },
    @{ Name = 'auto-emulator-only'; Options = @{ Device = $null }; Builds = 0; Installs = 0; Launches = 0; Cold = 0; Error = 'Could not select one physical USB phone' },
    @{ Name = 'auto-invalid-emulator'; Options = @{ Device = $null }; Builds = 0; Installs = 0; Launches = 0; Cold = 0; Error = 'Could not select one physical USB phone' },
    @{ Name = 'auto-offline'; Options = @{ Device = $null }; Builds = 0; Installs = 0; Launches = 0; Cold = 0; Error = 'offline or unauthorized' },
    @{ Name = 'auto-unauthorized'; Options = @{ Device = $null }; Builds = 0; Installs = 0; Launches = 0; Cold = 0; Error = 'offline or unauthorized' },
    @{ Name = 'auto-adb-failed'; Options = @{ Device = $null }; Builds = 0; Installs = 0; Launches = 0; Cold = 0; Error = 'Could not select one physical USB phone' },
    @{ Name = 'build-failed'; Options = @{}; Installs = 0; Launches = 0; Cold = 0; Error = 'Current-source Release build failed' },
    @{ Name = 'wrong-identity'; Options = @{}; Installs = 0; Launches = 0; Cold = 0; Error = 'identity/version mismatch' },
    @{ Name = 'debuggable'; Options = @{}; Installs = 0; Launches = 0; Cold = 0; Error = 'APK is debuggable' },
    @{ Name = 'wrong-abi'; Options = @{}; Installs = 0; Launches = 0; Cold = 0; Error = 'ABI mismatch' },
    @{ Name = 'missing-aot'; Options = @{}; Installs = 0; Launches = 0; Cold = 0; Error = 'AOT library missing' },
    @{ Name = 'bad-signature'; Options = @{}; Installs = 0; Launches = 0; Cold = 0; Error = 'signature verification failed' },
    @{ Name = 'install-failed'; Options = @{}; Installs = 1; Launches = 0; Cold = 0; Error = 'Release installation failed' }
)
try {
    foreach ($case in $installerCases) {
        $global:OtpHarborReleaseInstallerFixture = @{ Scenario = $case.Name; Calls = [Collections.Generic.List[string]]::new(); Targets = [Collections.Generic.List[string]]::new() }
        $options = @{ Device = 'synthetic-phone'; ArtifactDirectory = (Join-Path $installerTestRoot $case.Name)
            AdbPath = $installerAdb; AaptPath = $installerAapt; JavaPath = $installerJava; BuildToolsPath = $installerTools }
        foreach ($key in $case.Options.Keys) { $options[$key] = $case.Options[$key] }
        $failure = $null
        try { & (Join-Path $PSScriptRoot 'Install-AndroidReleasePhone.ps1') @options | Out-Null }
        catch { $failure = $_.Exception.Message }
        if (($null -eq $case.Error -and $null -ne $failure) -or
            ($null -ne $case.Error -and ($null -eq $failure -or $failure -notlike "*$($case.Error)*"))) {
            throw "Case $($case.Name) failed: $failure"
        }
        $calls = $global:OtpHarborReleaseInstallerFixture.Calls
        $expectedBuilds = if ($case.ContainsKey('Builds')) { $case.Builds } else { 1 }
        if (@($calls | Where-Object { $_ -eq 'build-current-source' }).Count -ne $expectedBuilds -or
            @($calls | Where-Object { $_ -like 'install -r *' }).Count -ne $case.Installs -or
            @($calls | Where-Object { $_ -like 'shell am start *' }).Count -ne $case.Launches -or
            @($calls | Where-Object { $_ -like 'shell am start * -S *' }).Count -ne $case.Cold) {
            throw "Unexpected build/install/launch counts for $($case.Name): $($calls -join '; ')"
        }
        if ($calls -match 'uninstall|\bclear\b') { throw 'The installer attempted to remove application data.' }
        $usesAutoDevice = [string]::IsNullOrWhiteSpace($options.Device) -and -not ($options.ContainsKey('BuildOnly') -and $options.BuildOnly)
        $expectedQueries = if ($usesAutoDevice) { 1 } else { 0 }
        if (@($calls | Where-Object { $_ -eq '-d get-serialno' }).Count -ne $expectedQueries) {
            throw "Unexpected physical-device query count for $($case.Name)."
        }
        $expectedTarget = if ($usesAutoDevice) { 'synthetic-physical-phone' } else { 'synthetic-phone' }
        if (@($global:OtpHarborReleaseInstallerFixture.Targets | Where-Object { $_ -ne $expectedTarget }).Count -gt 0) {
            throw "The installer targeted a different device for $($case.Name)."
        }
        Write-Output "PASS: $($case.Name)"
    }
}
finally { Remove-Variable -Name OtpHarborReleaseInstallerFixture -Scope Global -ErrorAction SilentlyContinue }

Write-Output "PASS: $($installerCases.Count) installer scenarios; no real build tools or devices accessed."
$global:LASTEXITCODE = 0
