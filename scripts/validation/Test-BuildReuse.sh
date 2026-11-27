#!/usr/bin/env bash
set -Eeuo pipefail
source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../shared" && pwd)/Build.Common.sh"
fixture="$(mktemp -d "${TMPDIR:-/tmp}/otp-harbor-build-reuse.XXXXXX")"
cleanup() {
    case "$fixture" in
        "${TMPDIR:-/tmp}"/otp-harbor-build-reuse.*) rm -rf -- "$fixture" ;;
        *) printf 'Unexpected fixture cleanup path.\n' >&2; return 1 ;;
    esac
}
trap cleanup EXIT
mkdir -p -- "$fixture/scripts" "$fixture/OtpHarbor.Core" "$fixture/artifacts/output"
printf 'synthetic source' > "$fixture/OtpHarbor.Core/Source.cs"
printf 'synthetic config' > "$fixture/global.json"
reuse_sdk='10.0.404'
dotnet() {
    if [[ "$1" == --version ]]; then printf '%s\n' "$reuse_sdk"; return; fi
    printf 'build\n' >> "$fixture/calls"
    [[ ! -f "$fixture/fail" ]] || return 7
    printf '%s\n' "$*" > "$fixture/artifacts/output/App.dll"
}
build() { otp_harbor_cached_build "$fixture" 'fixture' "$fixture/artifacts/output" -- build Synthetic.csproj -c "${1:-Release}"; }
assert_count() { [[ "$(wc -l < "$fixture/calls" | tr -d ' ')" == "$1" ]] || { printf 'Unexpected compilation count.\n' >&2; exit 1; }; }
build; assert_count 1
build; assert_count 1
touch -- "$fixture/OtpHarbor.Core/Source.cs"
build; assert_count 1
printf 'changed source' > "$fixture/OtpHarbor.Core/Source.cs"
build; assert_count 2
printf 'changed config' > "$fixture/global.json"
build; assert_count 3
build Debug; assert_count 4
printf 'changed output' > "$fixture/artifacts/output/App.dll"
build Debug; assert_count 5
rm -- "$fixture/artifacts/output/App.dll"
build Debug; assert_count 6
reuse_sdk='10.0.405'
build Debug; assert_count 7
touch -- "$fixture/fail"
if build; then printf 'A failed compiler was accepted.\n' >&2; exit 1; fi
assert_count 8
rm -- "$fixture/fail"
build Debug; assert_count 9
printf 'new file' > "$fixture/OtpHarbor.Core/Added.cs"
build Debug; assert_count 10
rm -- "$fixture/OtpHarbor.Core/Added.cs"
build Debug; assert_count 11
printf 'PASS: Bash unchanged content, source/config/argument/SDK/output changes, failed builds and file additions/removals.\n'
