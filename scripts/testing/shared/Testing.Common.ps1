function Get-OtpHarborRepositoryRoot {
    [CmdletBinding()]
    param([string]$StartDirectory = $PSScriptRoot)

    $directory = [IO.DirectoryInfo]::new([IO.Path]::GetFullPath($StartDirectory))
    while ($null -ne $directory) {
        if ((Test-Path -LiteralPath (Join-Path $directory.FullName 'OtpHarbor.sln') -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $directory.FullName 'scripts/testing') -PathType Container)) {
            return $directory.FullName
        }
        $directory = $directory.Parent
    }
    throw "Cannot find the OTP Harbor repository above '$StartDirectory'."
}

function Resolve-OtpHarborTestingPath {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RelativePath)

    $path = Join-Path (Get-OtpHarborRepositoryRoot) "scripts/testing/$RelativePath"
    return (Resolve-Path -LiteralPath $path -ErrorAction Stop).Path
}
