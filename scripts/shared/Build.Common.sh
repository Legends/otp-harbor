#!/usr/bin/env bash

otp_harbor_input_hash() {
    local root="$1"; shift
    local -a paths=("$root/scripts")
    local path
    for path in "$root"/OtpHarbor.* "$root"/*.props "$root"/*.targets "$root"/*.json "$root"/*.config \
        "$root"/*.ps1 "$root"/*.sh "$root"/*.cmd "$root"/*.bat "$root"/*.py \
        "$root"/LICENSE.txt "$root"/LICENSING.md "$root"/THIRD_PARTY_NOTICES.md; do
        [[ ! -e "$path" ]] || paths+=("$path")
    done
    local sdk
    sdk="$(dotnet --version)" || return "$?"
    [[ -n "$sdk" ]] || return 1
    local revision='source archive'
    if [[ -e "$root/.git" ]] && command -v git >/dev/null; then
        revision="$(git -C "$root" rev-parse HEAD)" || return "$?"
        [[ "$revision" =~ ^[0-9a-fA-F]{40,64}$ ]] || return 1
    fi
    if [[ -n "$(find "${paths[@]}" -type d \( -name bin -o -name obj -o -name artifacts -o -name node_modules \
        -o -name __pycache__ -o -name .git -o -name .codex \) -prune -o -type l -print -quit)" ]]; then
        printf 'Build inputs must be regular files and directories.\n' >&2
        return 1
    fi
    {
        printf 'build-reuse-bash-v1\0%s\0' "$sdk" "$revision" "$(uname -sm)" "${DOTNET_ROOT:-}" "${NUGET_PACKAGES:-}" "$@"
        find "${paths[@]}" -type d \( -name bin -o -name obj -o -name artifacts -o -name node_modules \
            -o -name __pycache__ -o -name .git -o -name .codex \) -prune -o -type f -print0 |
            sort -z | xargs -0 -r sha256sum
    } | sha256sum | cut -d ' ' -f 1
}

otp_harbor_output_hash() {
    local output="$1"
    [[ -e "$output" && ! -L "$output" ]] || return 1
    [[ -n "$(find "$output" -type f -print -quit)" ]] || return 1
    [[ -z "$(find "$output" -type l -print -quit)" ]] || return 1
    find "$output" -type f -print0 | sort -z | xargs -0 -r sha256sum | sha256sum | cut -d ' ' -f 1
}

otp_harbor_cached_build() (
    set -Eeuo pipefail
    local root="$1" name="$2" output="$3"; shift 3
    [[ "$1" == -- ]] || { printf 'Missing cached-build argument separator.\n' >&2; exit 2; }
    shift
    umask 077
    local cache="$root/artifacts/build-cache"
    mkdir -p -- "$cache"
    local id stamp lock before after outputs recorded_input recorded_output owner
    id="$(printf '%s' "$name" | sha256sum | cut -d ' ' -f 1)"
    stamp="$cache/$id.bash"
    lock="$stamp.lock.d"
    if ! mkdir -- "$lock" 2>/dev/null; then
        owner="$(cat "$lock/pid" 2>/dev/null || true)"
        if [[ "$owner" =~ ^[0-9]+$ ]] && ! kill -0 "$owner" 2>/dev/null; then
            rm -f -- "$lock/pid"
            rmdir -- "$lock"
            mkdir -- "$lock"
        else
            printf 'Another build is using this cache. Retry after it finishes.\n' >&2
            exit 1
        fi
    fi
    printf '%s\n' "$BASHPID" > "$lock/pid"
    trap 'rm -f -- "$lock/pid"; rmdir -- "$lock"' EXIT
    before="$(otp_harbor_input_hash "$root" "$@")" || exit "$?"
    outputs="$(otp_harbor_output_hash "$output" || true)"
    recorded_input=""; recorded_output=""
    if [[ -f "$stamp" ]]; then
        { read -r recorded_input; read -r recorded_output; } < "$stamp" || true
    fi
    local can_reuse=1
    if [[ -e "$root/.git" ]] && ! command -v git >/dev/null; then can_reuse=0; fi
    if [[ "$can_reuse" == 1 && -n "$outputs" && "$before" == "$recorded_input" && "$outputs" == "$recorded_output" ]]; then
        printf 'Reusing verified build: %s (inputs and outputs unchanged).\n' "$name"
        exit 0
    fi
    rm -f -- "$stamp"
    dotnet "$@" || exit "$?"
    outputs="$(otp_harbor_output_hash "$output")" || exit "$?"
    after="$(otp_harbor_input_hash "$root" "$@")" || exit "$?"
    [[ "$before" == "$after" ]] || { printf 'Build inputs changed during compilation; retry before running.\n' >&2; exit 1; }
    printf '%s\n%s\n' "$after" "$outputs" > "$stamp.$BASHPID.tmp"
    mv -f -- "$stamp.$BASHPID.tmp" "$stamp"
)

otp_harbor_build_for_run() {
    local root="$1" project="$2" configuration="$3"; shift 3
    local -a build_options=()
    local option artifacts=""
    while (( $# )); do
        option="$1"; shift
        case "$option" in
            --) break ;;
            --no-build) return 0 ;;
            --no-launch-profile) ;;
            --launch-profile|-lp|--environment|-e) shift ;;
            --configuration|-c) configuration="$1"; shift ;;
            --project) project="$1"; shift ;;
            --artifacts-path) artifacts="$1"; build_options+=("$option" "$1"); shift ;;
            --framework|-f|--runtime|-r|--arch|-a|--os|--verbosity|-v|--property) build_options+=("$option" "$1"); shift ;;
            *) build_options+=("$option") ;;
        esac
    done
    [[ "$project" == /* ]] || project="$root/$project"
    local output="$(dirname -- "$project")/bin/$configuration"
    [[ -z "$artifacts" ]] || output="$artifacts/bin/$(basename -- "${project%.csproj}")"
    otp_harbor_cached_build "$root" 'Linux desktop run' "$output" -- build "$project" \
        --configuration "$configuration" "${build_options[@]}"
}
