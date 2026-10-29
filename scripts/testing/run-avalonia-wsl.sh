#!/usr/bin/env bash

set -Eeuo pipefail

script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd -- "$script_directory/../.." && pwd)"
kernel_release="$(uname -r)"

if [[ -z "${WSL_DISTRO_NAME:-}" && ! "$kernel_release" =~ [Mm]icrosoft ]]; then
    printf 'This launcher is intended for WSL testing.\n' >&2
    exit 2
fi

scale_factor="${AVALONIA_GLOBAL_SCALE_FACTOR:-2}"
if [[ ! "$scale_factor" =~ ^(([1-9][0-9]*)([.][0-9]+)?|0[.][0-9]*[1-9][0-9]*)$ ]]; then
    printf 'AVALONIA_GLOBAL_SCALE_FACTOR must be a positive number.\n' >&2
    exit 2
fi

export AVALONIA_GLOBAL_SCALE_FACTOR="$scale_factor"

printf 'Starting OTP Harbor under WSL with Avalonia scale %s.\n' "$scale_factor"
cd -- "$repository_root"
exec dotnet run \
    --project OtpHarbor.UI.Avalonia.Desktop/OtpHarbor.UI.Avalonia.Desktop.csproj \
    --configuration Debug \
    "$@"
