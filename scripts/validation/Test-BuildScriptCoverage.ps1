#requires -Version 5.1
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $repositoryRoot 'scripts/shared/Build.Common.ps1')
$entryPoints = @{
    'Start-OTP-Harbor.ps1' = 'Invoke-OtpHarborCachedBuild'
    'scripts/release/android/New-AndroidRelease.ps1' = 'Invoke-OtpHarborCachedBuild'
    'scripts/release/windows/New-MicrosoftStoreMsix.ps1' = 'Invoke-OtpHarborCachedBuild'
    'scripts/dev/windows/Publish-And-Run-AvaloniaWindows.ps1' = 'Invoke-OtpHarborCachedBuild'
    'scripts/testing/android/Build-AndroidAotExperiment.ps1' = 'Invoke-OtpHarborCachedBuild'
    'scripts/testing/android/Install-AndroidReleasePhone.ps1' = 'Invoke-OtpHarborCachedBuild'
    'scripts/testing/android/Install-AndroidDevelopmentBuild.ps1' = 'Get-OtpHarborBuildFingerprint'
    'scripts/testing/android/Copy-AuthenticatorImportTestKitToAndroid.ps1' = 'Invoke-OtpHarborCachedBuild'
    'scripts/testing/windows/Prepare-StoreUpgradeTestKit.ps1' = 'Invoke-OtpHarborCachedBuild'
    'scripts/testing/windows/Test-StoreUpgradeTestKit.ps1' = 'Invoke-OtpHarborCachedBuild'
    'scripts/marketing/Prepare-DemoData.ps1' = 'Invoke-OtpHarborCachedBuild'
    'scripts/testing/linux/run-avalonia-wsl.sh' = 'otp_harbor_build_for_run'
    'scripts/testing/linux/Sync-Restore-Run-AvaloniaLinuxVm.ps1' = 'otp_harbor_cached_build'
}
foreach ($entry in $entryPoints.GetEnumerator()) {
    $text = Get-Content -LiteralPath (Join-Path $repositoryRoot $entry.Key) -Raw
    if (-not $text.Contains($entry.Value)) { throw "Build reuse is missing from $($entry.Key)." }
    if ($text -match 'dotnet (run|test)' -and $text -notmatch '--no-build') {
        throw "The runner in $($entry.Key) does not separate build reuse from execution."
    }
}
foreach ($source in Get-OtpHarborBuildFiles $repositoryRoot | Where-Object {
    ($_.DirectoryName -eq $repositoryRoot -or $_.FullName.StartsWith((Join-Path $repositoryRoot 'scripts'), [StringComparison]::OrdinalIgnoreCase)) -and
    $_.Extension -in '.ps1', '.sh', '.py', '.cmd', '.bat'
}) {
    $relativePath = $source.FullName.Substring($repositoryRoot.Length + 1).Replace('\', '/')
    if ($relativePath -like 'scripts/shared/Build.Common.*' -or $entryPoints.ContainsKey($relativePath)) { continue }
    if ($source.Extension -eq '.ps1') {
        $tokens = $null; $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile($source.FullName, [ref]$tokens, [ref]$errors)
        if ($errors.Count) { throw "Cannot inspect build commands in $relativePath." }
        $commands = $ast.FindAll({ param($node)
            $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'dotnet'
        }, $true)
        foreach ($command in $commands) {
            if ($command.CommandElements.Count -lt 2) { continue }
            $verb = $command.CommandElements[1]
            if ($verb -isnot [Management.Automation.Language.StringConstantExpressionAst] -or
                $verb.Value -in 'build', 'publish', 'test', 'run') {
                throw "Register and verify build reuse for $relativePath."
            }
        }
    }
    elseif ((Get-Content -LiteralPath $source.FullName -Raw) -match '(?m)^\s*(?:exec\s+)?dotnet\s+(?:build|publish|test|run)\b|["'']dotnet["'']\s*,\s*["''](?:build|publish|test|run)["'']') {
        throw "Register and verify build reuse for $relativePath."
    }
}
Write-Output "PASS: all $($entryPoints.Count) build entry points use content-based reuse and runners retain execution."
