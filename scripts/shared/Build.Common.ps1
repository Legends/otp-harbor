# Build reuse is local only. Install, test, package and signature checks still run.
function Get-OtpHarborTextHash {
    param([string]$Text)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($algorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text)))).Replace('-', '') }
    finally { $algorithm.Dispose() }
}

function Get-OtpHarborBuildFiles {
    param([string]$RepositoryRoot)
    Get-ChildItem -LiteralPath $RepositoryRoot -File | Where-Object {
        $_.Extension -in '.props', '.targets', '.json', '.config', '.sln', '.slnx', '.ps1', '.sh', '.cmd', '.bat', '.py' -or
        $_.Name -in 'LICENSE.txt', 'LICENSING.md', 'THIRD_PARTY_NOTICES.md'
    }
    $pending = [Collections.Generic.Queue[string]]::new()
    Get-ChildItem -LiteralPath $RepositoryRoot -Directory | Where-Object {
        $_.Name -like 'OtpHarbor.*' -or $_.Name -eq 'scripts'
    } | ForEach-Object { $pending.Enqueue($_.FullName) }
    while ($pending.Count -gt 0) {
        foreach ($item in Get-ChildItem -LiteralPath $pending.Dequeue() -Force) {
            if ($item.PSIsContainer -and $item.Name -in 'bin', 'obj', 'artifacts', 'node_modules', '__pycache__', '.git', '.codex') { continue }
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw 'Build inputs must be regular files and directories.'
            }
            if ($item.PSIsContainer) {
                if ($item.Name -notin 'bin', 'obj', 'artifacts', 'node_modules', '__pycache__', '.git', '.codex') {
                    $pending.Enqueue($item.FullName)
                }
            }
            else { $item }
        }
    }
}

function Get-OtpHarborBuildFingerprint {
    param([string]$RepositoryRoot, [string[]]$CommandArguments, [string[]]$AdditionalInputPaths = @())
    $sdk = (& dotnet --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sdk)) { throw 'Cannot identify the build SDK.' }
    $records = [Collections.Generic.List[string]]::new()
    $records.Add('build-reuse-v1')
    $records.Add($sdk)
    $records.Add([Environment]::OSVersion.ToString())
    $records.Add(($CommandArguments | ConvertTo-Json -Compress))
    if ((Test-Path -LiteralPath (Join-Path $RepositoryRoot '.git')) -and (Get-Command git -ErrorAction SilentlyContinue)) {
        $revision = (& git -C $RepositoryRoot rev-parse HEAD | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or $revision -notmatch '^[0-9a-fA-F]{40,64}$') { throw 'Cannot identify the checked-out source revision.' }
        $records.Add("SourceRevision=$revision")
    }
    foreach ($name in 'DOTNET_ROOT', 'ANDROID_HOME', 'ANDROID_SDK_ROOT', 'ANDROID_NDK_HOME', 'JAVA_HOME', 'NUGET_PACKAGES') {
        $records.Add("$name=$([Environment]::GetEnvironmentVariable($name))")
    }
    if (($CommandArguments -join ' ') -match '(?i)android') {
        $workload = (& dotnet workload --version | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($workload)) { throw 'Cannot identify the Android workload.' }
        $records.Add($workload)
    }
    $files = @(Get-OtpHarborBuildFiles $RepositoryRoot)
    if (($CommandArguments -join ' ') -match '(?i)android') {
        $sdkRoots = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT)
        if ($env:LOCALAPPDATA) { $sdkRoots += Join-Path $env:LOCALAPPDATA 'Android/Sdk' }
        foreach ($sdkRoot in $sdkRoots | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique) {
            foreach ($component in 'build-tools', 'platforms') {
                $componentRoot = Join-Path $sdkRoot $component
                if (-not (Test-Path -LiteralPath $componentRoot -PathType Container)) { continue }
                foreach ($version in Get-ChildItem -LiteralPath $componentRoot -Directory) {
                    $metadata = Join-Path $version.FullName 'source.properties'
                    if (Test-Path -LiteralPath $metadata -PathType Leaf) { $files += Get-Item -LiteralPath $metadata }
                }
            }
        }
        $javaMetadata = if ($env:JAVA_HOME) { Join-Path $env:JAVA_HOME 'release' } else { $null }
        if ($javaMetadata -and (Test-Path -LiteralPath $javaMetadata -PathType Leaf)) { $files += Get-Item -LiteralPath $javaMetadata }
    }
    foreach ($path in $AdditionalInputPaths) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'A required build input is missing.' }
        $files += Get-Item -LiteralPath $path
    }
    $orderedFiles = [Collections.Generic.SortedDictionary[string, IO.FileInfo]]::new([StringComparer]::Ordinal)
    foreach ($file in $files) { $orderedFiles[$file.FullName] = $file }
    foreach ($file in $orderedFiles.Values) {
        $records.Add($file.FullName + "`t" + (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash)
    }
    Get-OtpHarborTextHash (ConvertTo-Json -InputObject $records.ToArray() -Compress)
}

function Get-OtpHarborBuildOutputFingerprint {
    param([string[]]$OutputPaths)
    $records = [Collections.Generic.List[string]]::new()
    foreach ($path in $OutputPaths) {
        if (-not (Test-Path -LiteralPath $path)) { return $null }
        $item = Get-Item -LiteralPath $path
        $nodes = @(if ($item.PSIsContainer) { Get-ChildItem -LiteralPath $path -Recurse -Force } else { $item })
        if (@($nodes | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { return $null }
        $files = @($nodes | Where-Object { -not $_.PSIsContainer })
        if ($files.Count -eq 0 -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { return $null }
        $orderedFiles = [Collections.Generic.SortedDictionary[string, IO.FileInfo]]::new([StringComparer]::Ordinal)
        foreach ($file in $files) { $orderedFiles[$file.FullName] = $file }
        foreach ($file in $orderedFiles.Values) {
            if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { return $null }
            $records.Add($file.FullName + "`t" + (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash)
        }
    }
    if ($records.Count -eq 0) { return $null }
    Get-OtpHarborTextHash (ConvertTo-Json -InputObject $records.ToArray() -Compress)
}

function Invoke-OtpHarborCachedBuild {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$CacheName,
        [Parameter(Mandatory)][string[]]$CommandArguments,
        [Parameter(Mandatory)][string[]]$OutputPaths,
        [string[]]$AdditionalInputPaths = @(),
        [scriptblock]$BuildAction,
        [scriptblock]$OnOutputsReady,
        [switch]$Force
    )
    $ErrorActionPreference = 'Stop'
    $cacheDirectory = Join-Path $RepositoryRoot 'artifacts/build-cache'
    $null = New-Item -ItemType Directory -Path $cacheDirectory -Force
    $cacheId = Get-OtpHarborTextHash $CacheName
    $stampPath = Join-Path $cacheDirectory "$cacheId.json"
    try { $buildLock = [IO.File]::Open((Join-Path $cacheDirectory "$cacheId.lock"), 'OpenOrCreate', 'ReadWrite', 'None') }
    catch { throw 'Another build is using this cache. Retry after it finishes.' }
    try {
        $before = Get-OtpHarborBuildFingerprint $RepositoryRoot $CommandArguments $AdditionalInputPaths
        $outputs = Get-OtpHarborBuildOutputFingerprint $OutputPaths
        $stamp = $null
        if (Test-Path -LiteralPath $stampPath -PathType Leaf) {
            try { $stamp = Get-Content -LiteralPath $stampPath -Raw | ConvertFrom-Json }
            catch { Write-Verbose 'Ignoring invalid build reuse metadata.' }
        }
        $unresolvedRevision = (Test-Path -LiteralPath (Join-Path $RepositoryRoot '.git')) -and -not (Get-Command git -ErrorAction SilentlyContinue)
        if (-not $Force -and -not $unresolvedRevision -and $null -ne $stamp -and $outputs -and
            $stamp.PSObject.Properties.Name -contains 'InputHash' -and
            $stamp.PSObject.Properties.Name -contains 'OutputHash' -and
            $stamp.InputHash -is [string] -and $stamp.OutputHash -is [string] -and
            $stamp.InputHash -ceq $before -and $stamp.OutputHash -ceq $outputs) {
            Write-Host "Reusing verified build: $CacheName (inputs and outputs unchanged)."
            if ($null -ne $OnOutputsReady) { & $OnOutputsReady }
            $global:LASTEXITCODE = 0
            return
        }
        if (Test-Path -LiteralPath $stampPath) { Remove-Item -LiteralPath $stampPath -Force }
        Write-Host "Building: $CacheName (inputs or outputs changed)."
        if ($null -ne $BuildAction) {
            & $BuildAction
            if ($LASTEXITCODE -ne 0) { throw 'Build action failed; previous build reuse metadata was invalidated.' }
        }
        else {
            & dotnet @CommandArguments
            if ($LASTEXITCODE -ne 0) { throw 'Build failed; previous build reuse metadata was invalidated.' }
        }
        $outputs = Get-OtpHarborBuildOutputFingerprint $OutputPaths
        if (-not $outputs) { throw 'Build did not produce its required outputs.' }
        $after = Get-OtpHarborBuildFingerprint $RepositoryRoot $CommandArguments $AdditionalInputPaths
        if ($before -cne $after) { throw 'Build inputs changed during compilation. Retry before installing or publishing.' }
        # Only hashes are persisted; arguments and signing credentials are never recorded.
        $json = @{ InputHash = $after; OutputHash = $outputs } | ConvertTo-Json
        $temporaryStamp = "$stampPath.$([Guid]::NewGuid().ToString('N')).tmp"
        try {
            [IO.File]::WriteAllText($temporaryStamp, $json, [Text.UTF8Encoding]::new($false))
            Move-Item -LiteralPath $temporaryStamp -Destination $stampPath -Force
        }
        finally { if (Test-Path -LiteralPath $temporaryStamp) { Remove-Item -LiteralPath $temporaryStamp -Force } }
        # Staging copies run under the same lock so a different build cannot replace their source.
        if ($null -ne $OnOutputsReady) { & $OnOutputsReady }
        $global:LASTEXITCODE = 0
    }
    finally { $buildLock.Dispose() }
}
