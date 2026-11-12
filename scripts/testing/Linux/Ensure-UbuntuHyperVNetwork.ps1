<#
.SYNOPSIS
Creates or repairs the persistent host-side Hyper-V network used by the Ubuntu test VM.

.DESCRIPTION
Keeps the VM reachable at 192.168.250.10 and provides outbound internet access through
Windows NAT. Run from an elevated PowerShell session. The script is idempotent and refuses
to replace unexpected switches, addresses, NAT networks, or duplicate MAC assignments.
#>
[CmdletBinding()]
param(
    [string]$VmName = 'Ubuntu 26.04',
    [string]$SwitchName = 'Ubuntu Host-Only',
    [string]$AdapterName = 'Stable RDP',
    [ValidatePattern('^[0-9A-Fa-f]{12}$')]
    [string]$AdapterMacAddress = '02155D260410',
    [ValidatePattern('^\d{1,3}(?:\.\d{1,3}){3}$')]
    [string]$HostAddress = '192.168.250.1',
    [ValidateRange(1, 30)]
    [int]$PrefixLength = 24,
    [string]$NatName = 'UbuntuNAT',
    [string]$LegacyNatName = 'VMNAT',
    [string]$LegacyNatPrefix = '192.168.100.0/24'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$principal = [Security.Principal.WindowsPrincipal]::new(
    [Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell session.'
}

foreach ($command in @(
    'Get-VM',
    'Get-VMSwitch',
    'New-VMSwitch',
    'Get-VMNetworkAdapter',
    'Add-VMNetworkAdapter',
    'Connect-VMNetworkAdapter',
    'Set-VMNetworkAdapter',
    'Get-NetNat',
    'Get-NetNatStaticMapping',
    'New-NetNat',
    'Remove-NetNat',
    'Add-NetNatStaticMapping',
    'Get-NetIPAddress',
    'New-NetIPAddress',
    'Get-NetAdapter'
)) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "Required Windows command is unavailable: $command"
    }
}

function ConvertTo-UInt32Address {
    param([Parameter(Mandatory)][string]$Address)

    $parsed = $null
    if (-not [Net.IPAddress]::TryParse($Address, [ref]$parsed) -or
        $parsed.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork) {
        throw "Invalid IPv4 address: $Address"
    }

    $bytes = $parsed.GetAddressBytes()
    [Array]::Reverse($bytes)
    return [BitConverter]::ToUInt32($bytes, 0)
}

function Get-NetworkPrefix {
    param(
        [Parameter(Mandatory)][string]$Address,
        [Parameter(Mandatory)][int]$Length
    )

    $addressValue = ConvertTo-UInt32Address $Address
    $mask = [uint32]::MaxValue -shl (32 - $Length)
    $networkValue = $addressValue -band $mask
    $bytes = [BitConverter]::GetBytes($networkValue)
    [Array]::Reverse($bytes)
    return "$([Net.IPAddress]::new($bytes))/$Length"
}

$networkPrefix = Get-NetworkPrefix -Address $HostAddress -Length $PrefixLength
$vm = Get-VM -Name $VmName -ErrorAction Stop

$switch = Get-VMSwitch -Name $SwitchName -ErrorAction SilentlyContinue
if ($null -eq $switch) {
    $switch = New-VMSwitch -Name $SwitchName -SwitchType Internal
}
elseif ($switch.SwitchType -ne 'Internal') {
    throw "Hyper-V switch '$SwitchName' exists but is not an internal switch."
}

$interfaceAlias = "vEthernet ($SwitchName)"
$hostInterface = $null
for ($attempt = 0; $attempt -lt 20 -and $null -eq $hostInterface; $attempt++) {
    $hostInterface = Get-NetAdapter -Name $interfaceAlias -ErrorAction SilentlyContinue
    if ($null -eq $hostInterface) {
        Start-Sleep -Milliseconds 250
    }
}
if ($null -eq $hostInterface) {
    throw "Windows did not create the host adapter '$interfaceAlias'."
}

$interfaceAddresses = @(Get-NetIPAddress `
    -InterfaceIndex $hostInterface.ifIndex `
    -AddressFamily IPv4 `
    -ErrorAction SilentlyContinue)
$expectedAddress = $interfaceAddresses | Where-Object {
    $_.IPAddress -eq $HostAddress -and $_.PrefixLength -eq $PrefixLength
}
if ($null -eq $expectedAddress) {
    $unexpectedManualAddresses = @($interfaceAddresses | Where-Object {
        $_.PrefixOrigin -eq 'Manual' -and $_.IPAddress -ne $HostAddress
    })
    if ($unexpectedManualAddresses.Count -gt 0) {
        $addresses = $unexpectedManualAddresses.IPAddress -join ', '
        throw "Refusing to replace unexpected address(es) on '$interfaceAlias': $addresses"
    }
    New-NetIPAddress `
        -InterfaceIndex $hostInterface.ifIndex `
        -IPAddress $HostAddress `
        -PrefixLength $PrefixLength | Out-Null
}

$desiredNat = Get-NetNat -Name $NatName -ErrorAction SilentlyContinue
if ($null -ne $desiredNat -and $desiredNat.InternalIPInterfaceAddressPrefix -ne $networkPrefix) {
    throw "NAT '$NatName' uses unexpected prefix $($desiredNat.InternalIPInterfaceAddressPrefix)."
}

$removedLegacyNat = $null
$removedLegacyMappings = @()
$otherNats = @(Get-NetNat | Where-Object { $_.Name -ne $NatName })
foreach ($otherNat in $otherNats) {
    $isKnownLegacyNat = $otherNat.Name -eq $LegacyNatName -and
        $otherNat.InternalIPInterfaceAddressPrefix -eq $LegacyNatPrefix
    if (-not $isKnownLegacyNat) {
        throw "Refusing to replace unrelated NAT '$($otherNat.Name)' ($($otherNat.InternalIPInterfaceAddressPrefix))."
    }

    $legacyNetwork = $LegacyNatPrefix.Split('/')[0]
    $legacyOctets = $legacyNetwork.Split('.')
    $legacyAddressStem = "$($legacyOctets[0]).$($legacyOctets[1]).$($legacyOctets[2])."
    $legacyInterfaceAddresses = @(Get-NetIPAddress -AddressFamily IPv4 | Where-Object {
        $_.IPAddress.StartsWith($legacyAddressStem, [StringComparison]::Ordinal)
    })
    if ($legacyInterfaceAddresses.Count -gt 0) {
        throw "Refusing to remove legacy NAT '$LegacyNatName' because its subnet is still assigned to a host interface."
    }

    $removedLegacyNat = $otherNat
    $removedLegacyMappings = @(Get-NetNatStaticMapping -NatName $LegacyNatName)
    Remove-NetNat -Name $LegacyNatName -Confirm:$false
}

if ($null -eq $desiredNat) {
    try {
        New-NetNat -Name $NatName -InternalIPInterfaceAddressPrefix $networkPrefix | Out-Null
    }
    catch {
        if ($null -ne $removedLegacyNat) {
            New-NetNat `
                -Name $removedLegacyNat.Name `
                -InternalIPInterfaceAddressPrefix $removedLegacyNat.InternalIPInterfaceAddressPrefix | Out-Null
            foreach ($mapping in $removedLegacyMappings) {
                Add-NetNatStaticMapping `
                    -NatName $removedLegacyNat.Name `
                    -Protocol $mapping.Protocol `
                    -ExternalIPAddress $mapping.ExternalIPAddress `
                    -ExternalPort $mapping.ExternalPort `
                    -InternalIPAddress $mapping.InternalIPAddress `
                    -InternalPort $mapping.InternalPort `
                    -RemoteExternalIPAddressPrefix $mapping.RemoteExternalIPAddressPrefix | Out-Null
            }
        }
        throw
    }
}

$duplicateMacAdapters = @(Get-VMNetworkAdapter -All | Where-Object {
    $_.MacAddress -eq $AdapterMacAddress -and
        -not ($_.VMName -eq $VmName -and $_.Name -eq $AdapterName)
})
if ($duplicateMacAdapters.Count -gt 0) {
    throw "Static MAC address $AdapterMacAddress is assigned to another Hyper-V adapter."
}

$vmAdapter = Get-VMNetworkAdapter -VMName $VmName -Name $AdapterName -ErrorAction SilentlyContinue
if ($null -eq $vmAdapter) {
    Add-VMNetworkAdapter `
        -VMName $VmName `
        -Name $AdapterName `
        -SwitchName $SwitchName `
        -StaticMacAddress $AdapterMacAddress
    $vmAdapter = Get-VMNetworkAdapter -VMName $VmName -Name $AdapterName
}
elseif ($vmAdapter.SwitchName -ne $SwitchName) {
    Connect-VMNetworkAdapter -VMNetworkAdapter $vmAdapter -SwitchName $SwitchName
}

Set-VMNetworkAdapter `
    -VMName $VmName `
    -Name $AdapterName `
    -StaticMacAddress $AdapterMacAddress

$resultAdapter = Get-VMNetworkAdapter -VMName $VmName -Name $AdapterName
$resultNat = Get-NetNat -Name $NatName
$resultHostAddress = Get-NetIPAddress `
    -InterfaceIndex $hostInterface.ifIndex `
    -AddressFamily IPv4 | Where-Object { $_.IPAddress -eq $HostAddress }

[PSCustomObject]@{
    VM = $vm.Name
    Switch = $switch.Name
    HostAddress = "$($resultHostAddress.IPAddress)/$($resultHostAddress.PrefixLength)"
    Nat = $resultNat.Name
    NatPrefix = $resultNat.InternalIPInterfaceAddressPrefix
    VMAdapter = $resultAdapter.Name
    VMMacAddress = $resultAdapter.MacAddress
}
