# Persistent Ubuntu Hyper-V network

This document records the permanent network configuration for the `Ubuntu 26.04` development VM.
It avoids relying on Hyper-V's automatically managed **Default Switch**, whose subnet and DHCP
lease can change after a Windows restart, VM restore, or Hyper-V networking reset.

## Fixed topology

| Item | Persistent value |
| --- | --- |
| VM | `Ubuntu 26.04` |
| Hyper-V switch | `Ubuntu Host-Only` (internal) |
| Windows host address | `192.168.250.1/24` |
| Ubuntu address | `192.168.250.10/24` |
| Ubuntu gateway | `192.168.250.1` |
| Windows NAT | `UbuntuNAT`, `192.168.250.0/24` |
| VM adapter | `Stable RDP` |
| Stable adapter MAC | `02:15:5d:26:04:10` |
| DNS | Cloudflare `1.1.1.1`, Quad9 `9.9.9.9` |

The original `Network Adapter` remains connected to the Default Switch as a lower-priority DHCP
fallback. Scripts and RDP/SSH access should always use `192.168.250.10`.
`Sync-Restore-Run-AvaloniaLinuxVm.ps1` therefore uses this address by default; its `-VmHost`
parameter remains available for an intentional override.

## Root cause and applied repair

The stable internal switch and VM address already used `192.168.250.0/24`, but the only explicit
Windows NAT (`VMNAT`) still targeted the obsolete `192.168.100.0/24` subnet. No host interface used
that subnet. It also contained an orphaned TCP/8080 mapping to the absent `192.168.100.2` guest.
Consequently, the stable NIC could reach the Windows host but could not be translated to the
internet. The Default Switch did not report a current guest DHCP address, so it did not provide a
reliable fallback.

The existing Ubuntu `hyperv-host-only` profile also contained only the static address: its gateway
and DNS fields were empty and `ipv4.never-default` was enabled. The persistent
`OTP Harbor Hyper-V NAT` profile corrects those settings with gateway `192.168.250.1`, explicit
DNS, route metric `50`, and NetworkManager autoconnect priority `999` (the maximum accepted value).

On 2026-09-08 the orphaned `VMNAT` and its stale mapping were removed and replaced with:

```powershell
New-NetNat -Name UbuntuNAT -InternalIPInterfaceAddressPrefix 192.168.250.0/24
```

Hyper-V internal switches, manual host addresses, static VM MAC addresses, and `NetNat` objects are
persistent Windows configuration. The repair therefore survives VM shutdowns and host restarts.
No inbound NAT mapping is configured; the guest receives outbound connectivity only. The internal
switch is not bridged onto the physical LAN.

## Reapply or verify the Windows configuration

Open PowerShell **as Administrator** in the repository root and run:

```powershell
.\scripts\testing\Ensure-UbuntuHyperVNetwork.ps1
```

The script is idempotent. It creates missing expected components, corrects the named VM adapter,
and removes only the specifically recognized orphaned `VMNAT` configuration. It fails closed if it
finds an unrelated switch, address, NAT, or duplicate MAC instead of deleting unknown networking.
It deliberately leaves the Default Switch adapter in place as a fallback.

Host verification:

```powershell
Get-NetNat -Name UbuntuNAT
Get-NetIPAddress -InterfaceAlias 'vEthernet (Ubuntu Host-Only)' -AddressFamily IPv4
Get-VMNetworkAdapter -VMName 'Ubuntu 26.04' -Name 'Stable RDP'
Test-NetConnection 192.168.250.10 -Port 22
```

Expected results are NAT prefix `192.168.250.0/24`, host address `192.168.250.1`, VM MAC
`02155D260410`, and an open SSH port.

## Persist the Ubuntu route and DNS

Ubuntu Desktop uses NetworkManager. Run the guest helper once inside the VM:

```bash
cd ~/source/otp-harbor
sudo bash ./scripts/testing/configure-ubuntu-hyperv-network.sh
```

The helper identifies both interfaces by their Hyper-V MAC addresses instead of their potentially
changing Linux names. It creates a high-priority persistent connection for `192.168.250.10`, the
Windows NAT gateway, and explicit DNS. The Default Switch connection receives a higher route metric
and remains a fallback. Re-running the helper updates the same named connections.

Guest verification:

```bash
ip -brief address
ip route show default
ping -c 1 192.168.250.1
ping -c 1 1.1.1.1
getent ahostsv4 github.com
```

If the gateway succeeds but `1.1.1.1` fails, rerun the elevated Windows helper and inspect
`Get-NetNat`. If the IP ping succeeds but name resolution fails, inspect `resolvectl status` and
reactivate the connection with:

```bash
sudo nmcli connection up 'OTP Harbor Hyper-V NAT'
```

## Security and rollback

- The configuration does not expose the VM to the physical network and creates no inbound port
  forwarding.
- SSH and other guest services are reachable from the Windows host and other VMs attached to the
  same internal switch. Keep guest authentication and its firewall enabled.
- VPN software can intentionally block forwarded/NAT traffic. If host networking is correct but
  guest egress stops only while a VPN is connected, allow local-network access in that VPN rather
  than weakening the guest firewall.
- To remove only this permanent NAT, run elevated:

  ```powershell
  Remove-NetNat -Name UbuntuNAT -Confirm:$false
  ```

  The internal switch and static host/guest addresses remain available for host-only access.
