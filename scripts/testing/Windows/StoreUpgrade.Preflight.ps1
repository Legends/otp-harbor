function Invoke-UpgradePreflight {
    [CmdletBinding()]
    param([switch]$RequireStore, [switch]$RequireBuildTools, [string]$VerifierPath)

    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or -not [Environment]::Is64BitProcess) {
        throw 'Use 64-bit Windows PowerShell 5.1 or PowerShell 7 on Windows.'
    }
    if ($PSVersionTable.PSVersion -lt [version]'5.1') { throw 'PowerShell 5.1 or later is required.' }

    # A Windows PowerShell child can inherit PowerShell 7's module paths. Load the
    # modules shipped with this executable explicitly, without changing machine settings.
    foreach ($name in @('Microsoft.PowerShell.Management', 'Microsoft.PowerShell.Utility')) {
        $manifest = [IO.Path]::Combine($PSHOME, 'Modules', $name, "$name.psd1")
        if (-not [IO.File]::Exists($manifest)) {
            throw "Required built-in module $name is missing from $PSHOME. Repair this PowerShell installation; no test files were changed."
        }
        try { Import-Module -Name $manifest -Global -Force -ErrorAction Stop }
        catch { throw "Cannot load the built-in $name module for $PSHOME. Repair this PowerShell installation. No test files were changed." }
    }
    foreach ($command in @('Join-Path', 'Test-Path', 'Get-Item', 'Get-ChildItem', 'Get-Content', 'Set-Content',
            'Copy-Item', 'Move-Item', 'New-Item', 'Remove-Item', 'Get-Process', 'ConvertTo-Json', 'ConvertFrom-Json')) {
        if (-not (Get-Command -Name $command -ErrorAction SilentlyContinue)) {
            throw "Required built-in command $command is unavailable. Repair this PowerShell installation; no test files were changed."
        }
    }
    if ($RequireStore) {
        $manifest = [IO.Path]::Combine([Environment]::GetFolderPath('System'), 'WindowsPowerShell', 'v1.0', 'Modules', 'Appx', 'Appx.psd1')
        if (-not [IO.File]::Exists($manifest)) {
            throw 'The Windows Appx module is missing. Repair the Windows AppX deployment components before running this test. No test files were changed.'
        }
        try { Import-Module -Name $manifest -Global -ErrorAction Stop }
        catch { throw 'The Windows Appx module could not load. Run the supplied launcher in 64-bit Windows PowerShell 5.1. No test files were changed.' }
        if (-not (Get-Command Get-AppxPackage -ErrorAction SilentlyContinue)) {
            throw 'The Appx module does not provide Get-AppxPackage. Repair the Windows AppX deployment components.'
        }
    }
    if ($RequireBuildTools) {
        if (-not (Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue)) {
            throw 'The .NET 10 SDK is required to prepare or test the kit. Install it and reopen the terminal. The portable kit itself needs no SDK.'
        }
        $sdks = @(& dotnet --list-sdks)
        if ($LASTEXITCODE -ne 0 -or -not ($sdks -match '^10\.')) {
            throw 'The .NET 10 SDK was not found. Install it before preparing or testing the kit.'
        }
    }
    if ($VerifierPath) {
        if (-not [IO.File]::Exists($VerifierPath)) { throw 'The verifier executable is missing. Extract the complete kit again.' }
        try { & $VerifierPath self-check | Out-Null }
        catch { throw 'The packaged verifier could not start. Extract the complete Windows x64 kit again.' }
        if ($LASTEXITCODE -ne 0) { throw 'The verifier runtime or native cryptography dependencies failed their self-check. Extract the complete kit again.' }
    }
}
