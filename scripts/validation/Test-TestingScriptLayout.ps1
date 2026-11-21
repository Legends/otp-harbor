[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../testing/shared/Testing.Common.ps1')
$repositoryRoot = Get-OtpHarborRepositoryRoot -StartDirectory $PSScriptRoot
$scriptsRoot = Join-Path $repositoryRoot 'scripts'

# Parse and resolve references without running installers, device commands or vault operations.
$scriptCount = 0
foreach ($script in Get-ChildItem -LiteralPath $scriptsRoot -Filter '*.ps1' -File -Recurse) {
    $parseErrors = $null
    $tokens = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -gt 0) { throw "PowerShell syntax error in $($script.FullName): $parseErrors" }
    $commands = $ast.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] }, $true)
    foreach ($command in $commands) {
        $elements = $command.CommandElements
        if ($command.GetCommandName() -eq 'Join-Path' -and $elements.Count -eq 3 -and
            $elements[1].Extent.Text -in '$PSScriptRoot', '$scriptDirectory' -and
            $elements[2] -is [Management.Automation.Language.StringConstantExpressionAst]) {
            $relative = $elements[2].Value
            if ($relative -match '\.(ps1|py|sh|csproj|wxs|wxl|rtf|bmp)$' -or $relative -in 'fixtures', 'installer') {
                $path = Join-Path $script.DirectoryName $relative
                if (-not (Test-Path -LiteralPath $path)) { throw "Broken script-relative reference in $($script.Name): $relative" }
            }
        }
        if ($command.GetCommandName() -eq 'Resolve-OtpHarborTestingPath' -and $elements.Count -eq 2 -and
            $elements[1] -is [Management.Automation.Language.StringConstantExpressionAst]) {
            $null = Resolve-OtpHarborTestingPath $elements[1].Value
        }
    }
    if ((Get-OtpHarborRepositoryRoot -StartDirectory $script.DirectoryName) -ne $repositoryRoot) {
        throw "Unexpected repository root for $($script.FullName)."
    }
    $scriptCount++
}

$referenceCount = 0
foreach ($relativeDirectory in @('scripts', 'docs', '.github', 'artifacts/marketing', 'OtpHarbor.Tests/Fixtures')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repositoryRoot $relativeDirectory) -File -Recurse |
             Where-Object Extension -In '.ps1', '.sh', '.md', '.yml', '.yaml') {
        $content = [IO.File]::ReadAllText($file.FullName)
        # Policy tests deliberately refer to absent retired scripts; keep their
        # existing testing-reference checks and validate their execution separately.
        $referencePattern = if ($file.DirectoryName -eq (Join-Path $repositoryRoot 'scripts/validation')) {
            'scripts[\\/]+testing[\\/]+[A-Za-z0-9_./\\-]+\.(?:ps1|sh|md|json|2fas|csv|txt)\b'
        } else {
            'scripts[\\/]+[A-Za-z0-9_./\\-]+\.(?:ps1|py|sh|md|json|2fas|csv|txt)\b'
        }
        foreach ($match in [regex]::Matches($content, $referencePattern)) {
            $relative = $match.Value -replace '[\\/]+', '/'
            if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $relative) -PathType Leaf)) {
                throw "Broken testing-script reference in $($file.FullName): $relative"
            }
            $referenceCount++
        }
    }
}

# A source archive without .git, extra nesting and spaces must also resolve correctly.
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('otp-testing-layout-' + [guid]::NewGuid().ToString('N'))
$nested = Join-Path $temporaryRoot 'source archive/scripts/testing/android/nested'
$archiveRoot = Join-Path $temporaryRoot 'source archive'
try {
    [IO.Directory]::CreateDirectory($nested) | Out-Null
    [IO.File]::WriteAllText((Join-Path $archiveRoot 'OtpHarbor.sln'), '')
    Push-Location ([IO.Path]::GetTempPath())
    try {
        if ((Get-OtpHarborRepositoryRoot -StartDirectory $nested) -ne $archiveRoot) {
            throw 'Repository discovery failed for a nested source archive.'
        }
        if ((Get-OtpHarborRepositoryRoot) -ne $repositoryRoot) { throw 'Repository discovery depends on the working directory.' }
        $rejected = $false
        try { $null = Get-OtpHarborRepositoryRoot -StartDirectory $temporaryRoot }
        catch { $rejected = $true }
        if (-not $rejected) { throw 'Repository discovery accepted an unrelated directory.' }
    }
    finally { Pop-Location }
}
finally {
    # Remove only the exact synthetic files and empty directories created above.
    if (Test-Path -LiteralPath (Join-Path $archiveRoot 'OtpHarbor.sln')) {
        Remove-Item -LiteralPath (Join-Path $archiveRoot 'OtpHarbor.sln')
    }
    $directory = [IO.DirectoryInfo]::new($nested)
    while ($null -ne $directory -and $directory.FullName.StartsWith($temporaryRoot, [StringComparison]::Ordinal)) {
        if ($directory.Exists) { Remove-Item -LiteralPath $directory.FullName }
        $directory = $directory.Parent
    }
}

Write-Host "Validated $scriptCount repository scripts, $referenceCount documented/CI references and repository discovery."
