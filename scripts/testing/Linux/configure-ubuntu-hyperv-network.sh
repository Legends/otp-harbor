#!/usr/bin/env bash
set -Eeuo pipefail

stable_mac="${STABLE_MAC:-02:15:5d:26:04:10}"
fallback_mac="${FALLBACK_MAC:-00:15:5d:02:6d:10}"
guest_address="${GUEST_ADDRESS:-192.168.250.10/24}"
gateway="${GATEWAY:-192.168.250.1}"
dns_servers="${DNS_SERVERS:-1.1.1.1,9.9.9.9}"
stable_connection="OTP Harbor Hyper-V NAT"
fallback_connection="OTP Harbor Default Switch fallback"

if [[ "${EUID}" -ne 0 ]]; then
    printf 'Run this script with sudo.\n' >&2
    exit 1
fi
if ! command -v nmcli >/dev/null 2>&1; then
    printf 'NetworkManager/nmcli is required by this Ubuntu Desktop configuration.\n' >&2
    exit 1
fi

find_interface_by_mac() {
    local wanted_mac="${1,,}"
    local address_file
    for address_file in /sys/class/net/*/address; do
        if [[ "$(<"$address_file")" == "$wanted_mac" ]]; then
            basename "$(dirname "$address_file")"
            return 0
        fi
    done
    return 1
}

stable_interface="$(find_interface_by_mac "$stable_mac")" || {
    printf 'No interface with the stable Hyper-V MAC %s was found.\n' "$stable_mac" >&2
    exit 1
}

if nmcli --terse --fields NAME connection show | grep -Fxq "$stable_connection"; then
    nmcli connection modify "$stable_connection" \
        connection.interface-name "$stable_interface" \
        802-3-ethernet.mac-address "$stable_mac"
else
    nmcli connection add \
        type ethernet \
        ifname "$stable_interface" \
        con-name "$stable_connection" \
        802-3-ethernet.mac-address "$stable_mac"
fi

nmcli connection modify "$stable_connection" \
    connection.autoconnect yes \
    connection.autoconnect-priority 999 \
    ipv4.method manual \
    ipv4.addresses "$guest_address" \
    ipv4.gateway "$gateway" \
    ipv4.dns "$dns_servers" \
    ipv4.never-default no \
    ipv4.route-metric 50 \
    ipv6.method auto

if fallback_interface="$(find_interface_by_mac "$fallback_mac")"; then
    if nmcli --terse --fields NAME connection show | grep -Fxq "$fallback_connection"; then
        nmcli connection modify "$fallback_connection" \
            connection.interface-name "$fallback_interface" \
            802-3-ethernet.mac-address "$fallback_mac"
    else
        nmcli connection add \
            type ethernet \
            ifname "$fallback_interface" \
            con-name "$fallback_connection" \
            802-3-ethernet.mac-address "$fallback_mac"
    fi
    nmcli connection modify "$fallback_connection" \
        connection.autoconnect yes \
        connection.autoconnect-priority 100 \
        ipv4.method auto \
        ipv4.route-metric 500 \
        ipv6.method auto
fi

nmcli connection up "$stable_connection"

printf '\nPersistent Hyper-V network configured.\n'
printf 'Interface: %s (%s)\n' "$stable_interface" "$stable_mac"
printf 'Address:   %s\n' "$guest_address"
printf 'Gateway:   %s\n' "$gateway"
printf 'DNS:       %s\n\n' "$dns_servers"
ip -brief address show dev "$stable_interface"
ip route show default

ping -c 1 -W 3 "$gateway" >/dev/null
ping -c 1 -W 5 1.1.1.1 >/dev/null
getent ahostsv4 github.com >/dev/null
printf 'Gateway, outbound IP connectivity, and DNS resolution are working.\n'
