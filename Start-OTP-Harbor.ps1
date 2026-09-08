[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._/-]*$')]
    [string]$Branch = 'master',

    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string]$Remote = 'origin'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-OtpHarborLaunchPlan {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Current', 'Behind', 'Ahead', 'Diverged')]
        [string]$RepositoryState,

        [Parameter(Mandatory)]
        [bool]$HasLocalChanges,

        [Parameter(Mandatory)]
        [bool]$BuildIsCurrent
    )

    if ($HasLocalChanges) { return 'BlockedLocalChanges' }
    if ($RepositoryState -eq 'Behind') { return 'UpdateBuildAndRun' }
    if ($RepositoryState -eq 'Ahead') { return 'BlockedAhead' }
    if ($RepositoryState -eq 'Diverged') { return 'BlockedDiverged' }
    if ($BuildIsCurrent) { return 'Run' }
    return 'BuildAndRun'
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter(Mandatory)][string[]]$ArgumentList,
        [switch]$CaptureOutput
    )

    $output = @(& $Command @ArgumentList 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $details = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
        throw "$Command failed with exit code $LASTEXITCODE.$([Environment]::NewLine)$details"
    }

    if ($CaptureOutput) {
        return $output | ForEach-Object { $_.ToString() }
    }

    $output | ForEach-Object { Write-Host $_ }
}

function Get-GitText {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string[]]$ArgumentList
    )

    $output = Invoke-CheckedCommand -Command 'git' `
        -ArgumentList (@('-C', $RepositoryRoot) + $ArgumentList) `
        -CaptureOutput
    return ($output -join [Environment]::NewLine).Trim()
}

function Invoke-OtpHarborSourceLauncher {
    param(
        [Parameter(Mandatory)][string]$Configuration,
        [Parameter(Mandatory)][string]$Branch,
        [Parameter(Mandatory)][string]$Remote
    )

    foreach ($command in @('git', 'dotnet')) {
        if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
            throw "Required command '$command' was not found. Install Git and the .NET 10 SDK, then try again."
        }
    }

    $repositoryRoot = [IO.Path]::GetFullPath($PSScriptRoot)
    if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot '.git'))) {
        throw 'This launcher must be run from a Git clone of the official OTP Harbor repository.'
    }

    $projectPath = Join-Path $repositoryRoot 'TOTP.UI.Avalonia.Desktop/TOTP.UI.Avalonia.Desktop.csproj'
    $solutionPath = Join-Path $repositoryRoot 'TOTP.sln'
    $nugetConfigPath = Join-Path $repositoryRoot 'NuGet.config'
    foreach ($requiredPath in @($projectPath, $solutionPath, $nugetConfigPath)) {
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Required repository file is missing: $requiredPath"
        }
    }

    $remoteUrl = Get-GitText $repositoryRoot @('remote', 'get-url', $Remote)
    $approvedRemoteUrls = @(
        'https://github.com/Legends/otp-harbor',
        'https://github.com/Legends/otp-harbor.git',
        'git@github.com:Legends/otp-harbor.git',
        'ssh://git@github.com/Legends/otp-harbor.git'
    )
    if ($remoteUrl -notin $approvedRemoteUrls) {
        throw "Remote '$Remote' does not point to the official OTP Harbor repository: $remoteUrl"
    }

    $currentBranch = Get-GitText $repositoryRoot @('branch', '--show-current')
    if ($currentBranch -cne $Branch) {
        throw "The launcher updates branch '$Branch', but the checkout is on '$currentBranch'. Switch branches explicitly before continuing."
    }

    $installedSdks = @(Invoke-CheckedCommand -Command 'dotnet' `
        -ArgumentList @('--list-sdks') -CaptureOutput)
    if (-not ($installedSdks | Where-Object { $_ -match '^10\.' })) {
        throw 'The .NET 10 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/10.0.'
    }
    $selectedSdk = ((Invoke-CheckedCommand -Command 'dotnet' `
        -ArgumentList @('--version') -CaptureOutput) -join '').Trim()

    Write-Host "Checking $Remote/$Branch for OTP Harbor updates..."
    Invoke-CheckedCommand -Command 'git' -ArgumentList @(
        '-C', $repositoryRoot,
        'fetch', '--prune', '--no-tags', $Remote,
        "refs/heads/${Branch}:refs/remotes/${Remote}/${Branch}"
    )

    $localCommit = Get-GitText $repositoryRoot @('rev-parse', 'HEAD')
    $remoteCommit = Get-GitText $repositoryRoot @(
        'rev-parse', "refs/remotes/${Remote}/${Branch}")
    if ($localCommit -ceq $remoteCommit) {
        $repositoryState = 'Current'
    }
    else {
        & git -C $repositoryRoot merge-base --is-ancestor $localCommit $remoteCommit
        $localIsAncestorExitCode = $LASTEXITCODE
        if ($localIsAncestorExitCode -notin @(0, 1)) {
            throw "Git could not compare local and remote history (exit code $localIsAncestorExitCode)."
        }
        if ($localIsAncestorExitCode -eq 0) {
            $repositoryState = 'Behind'
        }
        else {
            & git -C $repositoryRoot merge-base --is-ancestor $remoteCommit $localCommit
            $remoteIsAncestorExitCode = $LASTEXITCODE
            if ($remoteIsAncestorExitCode -notin @(0, 1)) {
                throw "Git could not compare remote and local history (exit code $remoteIsAncestorExitCode)."
            }
            $repositoryState = if ($remoteIsAncestorExitCode -eq 0) {
                'Ahead'
            }
            else {
                'Diverged'
            }
        }
    }

    $hasLocalChanges = -not [string]::IsNullOrWhiteSpace(
        (Get-GitText $repositoryRoot @('status', '--porcelain', '--untracked-files=normal')))
    $stateDirectory = Join-Path $repositoryRoot 'artifacts/source-launcher'
    $statePath = Join-Path $stateDirectory "build-$Configuration.json"
    $buildState = if (Test-Path -LiteralPath $statePath -PathType Leaf) {
        try { Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json }
        catch { $null }
    }
    else {
        $null
    }
    $buildIsCurrent = $false
    if ($null -ne $buildState) {
        $requiredStateProperties = @('Commit', 'Configuration', 'DotNetSdk', 'TargetPath')
        $stateIsComplete = @($requiredStateProperties | Where-Object {
            $null -eq $buildState.PSObject.Properties[$_]
        }).Count -eq 0
        if ($stateIsComplete) {
            $buildIsCurrent = $buildState.Commit -ceq $localCommit `
                -and $buildState.Configuration -ceq $Configuration `
                -and $buildState.DotNetSdk -ceq $selectedSdk `
                -and (Test-Path -LiteralPath $buildState.TargetPath -PathType Leaf)
        }
    }

    $plan = Get-OtpHarborLaunchPlan -RepositoryState $repositoryState `
        -HasLocalChanges $hasLocalChanges -BuildIsCurrent $buildIsCurrent
    switch ($plan) {
        'BlockedLocalChanges' {
            throw 'Local changes are present. Commit, stash, or remove them before using the automatic source updater; no files were changed.'
        }
        'BlockedAhead' {
            throw "Local branch '$Branch' contains commits not present on $Remote/$Branch. The launcher will not overwrite or publish them."
        }
        'BlockedDiverged' {
            throw "Local branch '$Branch' has diverged from $Remote/$Branch. Resolve the Git history manually before continuing."
        }
        'UpdateBuildAndRun' {
            Write-Host 'A newer source revision is available. Applying a fast-forward update...'
            Invoke-CheckedCommand -Command 'git' -ArgumentList @(
                '-C', $repositoryRoot, 'merge', '--ff-only', "$Remote/$Branch")
            $localCommit = Get-GitText $repositoryRoot @('rev-parse', 'HEAD')
        }
        'BuildAndRun' {
            Write-Host 'No verified build exists for the current source revision.'
        }
        'Run' {
            Write-Host 'OTP Harbor source and build are already current.'
        }
    }

    if ($plan -in @('UpdateBuildAndRun', 'BuildAndRun')) {
        Write-Host "Restoring and compiling OTP Harbor ($Configuration)..."
        Invoke-CheckedCommand -Command 'dotnet' -ArgumentList @(
            'restore', $solutionPath, '--configfile', $nugetConfigPath,
            '--nologo', '--verbosity', 'minimal')
        Invoke-CheckedCommand -Command 'dotnet' -ArgumentList @(
            'build', $projectPath, '--configuration', $Configuration,
            '--no-restore', '--nologo', '--verbosity', 'minimal')

        $targetOutput = @(Invoke-CheckedCommand -Command 'dotnet' -ArgumentList @(
            'msbuild', $projectPath, '-nologo', '-getProperty:TargetPath',
            "-property:Configuration=$Configuration") -CaptureOutput)
        $targetPath = ($targetOutput | Where-Object {
            -not [string]::IsNullOrWhiteSpace($_)
        } | Select-Object -Last 1).Trim()
        if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
            throw "Compilation completed without the expected application output: $targetPath"
        }

        New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
        [ordered]@{
            Commit = $localCommit
            Configuration = $Configuration
            DotNetSdk = $selectedSdk
            TargetPath = $targetPath
            BuiltUtc = [DateTimeOffset]::UtcNow.ToString('O')
        } | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding utf8
    }

    Write-Host 'Starting OTP Harbor...'
    Push-Location $repositoryRoot
    try {
        & dotnet run --project $projectPath --configuration $Configuration --no-build --no-restore
        if ($LASTEXITCODE -ne 0) {
            throw "OTP Harbor exited with code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    Invoke-OtpHarborSourceLauncher -Configuration $Configuration -Branch $Branch -Remote $Remote
}
