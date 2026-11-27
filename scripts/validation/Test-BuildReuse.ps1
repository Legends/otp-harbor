#requires -Version 5.1
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '../shared/Build.Common.ps1')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('otp-harbor-build-reuse-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $testRoot 'OtpHarbor.Core/Source.cs'
$output = Join-Path $testRoot 'artifacts/output/App.dll'
$external = Join-Path $testRoot 'external.props'
$null = New-Item -ItemType Directory -Path (Split-Path $source), (Split-Path $output) -Force
[IO.File]::WriteAllText($source, 'synthetic source')
[IO.File]::WriteAllText((Join-Path $testRoot 'global.json'), '{"sdk":"synthetic"}')
[IO.File]::WriteAllText($external, 'synthetic external settings')
$script:reuseSdk = '10.0.404'
$script:reuseBuilds = 0
$script:reuseFail = $false
$script:reuseChangeDuringBuild = $false
function dotnet {
    if ($args[0] -ne '--version') { throw 'Only SDK inspection is allowed in this fixture.' }
    $global:LASTEXITCODE = 0
    $script:reuseSdk
}
$buildAction = {
    $script:reuseBuilds++
    if ($script:reuseFail) { throw 'Synthetic compiler failure.' }
    [IO.File]::WriteAllText($output, "Synthetic output $script:reuseBuilds")
    if ($script:reuseChangeDuringBuild) { [IO.File]::WriteAllText($source, 'Changed during compilation') }
}
function Invoke-FixtureBuild {
    param([string]$Mode = 'Release', [switch]$Force)
    Invoke-OtpHarborCachedBuild -RepositoryRoot $testRoot -CacheName 'fixture' `
        -CommandArguments @('publish', 'Synthetic.csproj', '-c', $Mode) -OutputPaths @($output) `
        -AdditionalInputPaths @($external) -BuildAction $buildAction -Force:$Force
}
function Assert-BuildCount([int]$Expected) {
    if ($script:reuseBuilds -ne $Expected) { throw "Expected $Expected compilations, found $script:reuseBuilds." }
}
try {
    Invoke-FixtureBuild; Assert-BuildCount 1
    Invoke-FixtureBuild; Assert-BuildCount 1
    (Get-Item -LiteralPath $source).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(1)
    Invoke-FixtureBuild; Assert-BuildCount 1
    [IO.File]::WriteAllText($source, 'Changed source')
    Invoke-FixtureBuild; Assert-BuildCount 2
    [IO.File]::WriteAllText((Join-Path $testRoot 'global.json'), '{"sdk":"changed"}')
    Invoke-FixtureBuild; Assert-BuildCount 3
    [IO.File]::WriteAllText($external, 'Changed external settings')
    Invoke-FixtureBuild; Assert-BuildCount 4
    Invoke-FixtureBuild -Mode Debug; Assert-BuildCount 5
    [IO.File]::WriteAllText($output, 'Changed output')
    Invoke-FixtureBuild -Mode Debug; Assert-BuildCount 6
    Remove-Item -LiteralPath $output
    Invoke-FixtureBuild -Mode Debug; Assert-BuildCount 7
    $script:reuseSdk = '10.0.405'
    Invoke-FixtureBuild -Mode Debug; Assert-BuildCount 8
    Invoke-FixtureBuild -Mode Debug -Force; Assert-BuildCount 9
    $script:reuseFail = $true
    $failed = $false
    try { Invoke-FixtureBuild -Force } catch { $failed = $true }
    if (-not $failed) { throw 'Compiler failure was not propagated.' }
    $script:reuseFail = $false
    Invoke-FixtureBuild -Mode Debug; Assert-BuildCount 11
    $script:reuseChangeDuringBuild = $true
    $failed = $false
    try { Invoke-FixtureBuild -Force } catch { $failed = $true }
    if (-not $failed) { throw 'Source changes during compilation were accepted.' }
    $script:reuseChangeDuringBuild = $false
    Invoke-FixtureBuild -Mode Debug; Assert-BuildCount 13
    [IO.File]::WriteAllText($source, 'Added or removed files must invalidate the cache')
    $added = Join-Path (Split-Path $source) 'Added.cs'
    [IO.File]::WriteAllText($added, 'New source file')
    Invoke-FixtureBuild -Mode Debug; Assert-BuildCount 14
    Remove-Item -LiteralPath $added
    Invoke-FixtureBuild -Mode Debug; Assert-BuildCount 15
    $stamp = Get-ChildItem -LiteralPath (Join-Path $testRoot 'artifacts/build-cache') -Filter '*.json' | Select-Object -First 1
    [IO.File]::WriteAllText($stamp.FullName, 'invalid metadata')
    Invoke-FixtureBuild -Mode Debug; Assert-BuildCount 16
    $localization = Join-Path $testRoot 'OtpHarbor.Localization/Resources/Mobile.resx'
    $null = New-Item -ItemType Directory -Path (Split-Path $localization) -Force
    [IO.File]::WriteAllText($localization, 'Changed localized build input')
    Invoke-FixtureBuild -Mode Debug; Assert-BuildCount 17
    Invoke-OtpHarborCachedBuild -RepositoryRoot $testRoot -CacheName 'fixture' `
        -CommandArguments @('publish', 'Synthetic.csproj', '-p:SigningCredential=synthetic-secret-marker') `
        -OutputPaths @($output) -AdditionalInputPaths @($external) -BuildAction $buildAction
    if ((Get-Content -LiteralPath $stamp.FullName -Raw).Contains('synthetic-secret-marker')) {
        throw 'Build reuse metadata exposed a credential argument.'
    }
    $script:readyCopies = 0
    $fixtureLockPath = [IO.Path]::ChangeExtension($stamp.FullName, '.lock')
    $stage = {
        $locked = $false
        try {
            $probe = [IO.File]::Open($fixtureLockPath, 'OpenOrCreate', 'ReadWrite', 'None')
            $probe.Dispose()
        }
        catch [IO.IOException] { $locked = $true }
        if (-not $locked) { throw 'Staging must run while the build output is locked.' }
        $script:readyCopies++
        Copy-Item -LiteralPath $output -Destination (Join-Path $testRoot "artifacts/stage-$script:readyCopies.dll")
    }
    foreach ($copy in 1..2) {
        Invoke-OtpHarborCachedBuild -RepositoryRoot $testRoot -CacheName 'fixture' `
            -CommandArguments @('publish', 'Synthetic.csproj', '-p:SigningCredential=synthetic-secret-marker') `
            -OutputPaths @($output) -AdditionalInputPaths @($external) -BuildAction $buildAction -OnOutputsReady $stage
    }
    Assert-BuildCount 18
    if ($script:readyCopies -ne 2 -or
        (Get-FileHash -LiteralPath (Join-Path $testRoot 'artifacts/stage-1.dll')).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $testRoot 'artifacts/stage-2.dll')).Hash) {
        throw 'Cached compilation must still copy each requested staging output.'
    }
    $expectedBuilds = 18
    foreach ($field in 'InputHash', 'OutputHash') {
        $currentStamp = Get-Content -LiteralPath $stamp.FullName -Raw | ConvertFrom-Json
        $invalidStamp = @{ InputHash = $currentStamp.InputHash; OutputHash = $currentStamp.OutputHash }
        $invalidStamp[$field] = @($invalidStamp[$field])
        [IO.File]::WriteAllText($stamp.FullName, ($invalidStamp | ConvertTo-Json -Depth 4))
        Invoke-OtpHarborCachedBuild -RepositoryRoot $testRoot -CacheName 'fixture' `
            -CommandArguments @('publish', 'Synthetic.csproj', '-p:SigningCredential=synthetic-secret-marker') `
            -OutputPaths @($output) -AdditionalInputPaths @($external) -BuildAction $buildAction
        $expectedBuilds++
        Assert-BuildCount $expectedBuilds
    }
    Write-Output 'PASS: unchanged content, source/config/external input/arguments/SDK/output invalidation, forced/failed/interrupted builds, file additions/removals and corrupt metadata.'
}
finally {
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    $allowed = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar + 'otp-harbor-build-reuse-'
    if (-not $resolvedTestRoot.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected fixture cleanup path.' }
    Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
}
