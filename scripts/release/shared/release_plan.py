"""Release selection and gates. No publication, credentials, or version remapping."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re

STABLE = r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)"
PLATFORMS = {
    "windows": {"group": "desktop", "production": True, "architectures": ["x64"], "channels": ["microsoft-store"]},
    "linux": {"group": "desktop", "production": True, "architectures": ["x64"], "channels": ["github"]},
    "android": {"group": "mobile", "production": True, "architectures": ["arm64-v8a", "x86_64"], "channels": ["github", "project-fdroid"]},
    "macos": {"group": "desktop", "production": False},
    "ios": {"group": "mobile", "production": False},
}


def version(value: str) -> tuple[int, int, int]:
    if not isinstance(value, str) or not re.fullmatch(STABLE, value, flags=re.ASCII):
        raise ValueError("Expected a canonical stable X.Y.Z version.")
    return tuple(map(int, value.split(".")))


def stable_tag(value: str) -> tuple[str, tuple[int, int, int]]:
    for family, prefix in (("desktop", "v"), ("android", "android-v")):
        if isinstance(value, str) and value.startswith(prefix):
            return family, version(value[len(prefix):])
    raise ValueError("Expected vX.Y.Z or android-vX.Y.Z.")


def resolve_version(requested: str, family: str, occupied: list[str]) -> str:
    used = []
    for tag in occupied:
        try:
            tag_family, number = stable_tag(tag)
        except ValueError:
            continue  # Historical RC/four-component namespaces are immutable, not new candidates.
        if tag_family == family:
            used.append(number)
    latest = max(used, default=(0, 0, 0))
    number = (latest[0], latest[1], latest[2] + 1) if requested == "auto" else version(requested)
    if number <= latest:
        raise ValueError(f"{family} version is occupied or does not advance upgrade ordering.")
    return ".".join(map(str, number))


def make_plan(*, scope: str, desktop: str, android: str, mode: str, store_action: str,
              source: str, ref: str, occupied: list[str], run_id: str, attempt: str,
              automatic_enabled: bool = False) -> dict:
    if scope not in ("desktop", "mobile", "all") or mode not in ("validate", "approval", "automatic"):
        raise ValueError("Invalid release scope or mode.")
    if store_action not in ("prepare", "submit"):
        raise ValueError("Invalid Store action.")
    if not re.fullmatch(r"[0-9a-f]{40}", source) or not re.fullmatch(r"refs/heads/[A-Za-z0-9][A-Za-z0-9/._-]*", ref):
        raise ValueError("Release orchestration requires an immutable branch source.")
    if mode != 'validate' and ref != 'refs/heads/master':
        raise ValueError('Production requires protected master; branch candidates are validation only.')
    if not re.fullmatch(r"[1-9][0-9]*", run_id) or not re.fullmatch(r"[1-9][0-9]*", attempt):
        raise ValueError("Invalid workflow identity.")
    if mode == "automatic" and not automatic_enabled:
        raise ValueError("Automatic promotion is disabled by trusted repository policy.")
    if mode == "validate" and store_action == "submit":
        raise ValueError("Validation cannot submit to Partner Center.")
    if scope == "mobile" and (store_action != "prepare" or desktop != "auto"):
        raise ValueError("Mobile scope cannot request Windows submission or a desktop version.")
    if scope == "desktop" and android != "auto":
        raise ValueError("Desktop scope cannot request an Android version.")
    platforms = [name for name, item in PLATFORMS.items()
                 if item["production"] and (scope == "all" or item["group"] == scope)]
    return {
        "schemaVersion": 1, "candidateId": f"{run_id}-{attempt}",
        "sourceCommit": source, "sourceRef": ref, "scope": scope, "mode": mode,
        "storeAction": store_action, "workflowRunId": run_id, "workflowRunAttempt": attempt,
        "desktopVersion": resolve_version(desktop, "desktop", occupied) if "windows" in platforms else None,
        "androidVersion": resolve_version(android, "android", occupied) if "android" in platforms else None,
        "platforms": platforms, "disabledPlatforms": [p for p, v in PLATFORMS.items() if not v["production"]],
        "productionEligible": mode != "validate",
    }


def check_gate(results: dict[str, str], required: list[str], optional: list[str] = ()) -> None:
    if set(results) != set(required) | set(optional):
        raise ValueError("Gate job set is incomplete or unexpected.")
    failed = [job for job in required if results[job] != "success"]
    failed += [job for job in optional if results[job] not in ("success", "skipped")]
    if failed:
        raise ValueError("Release gate rejected jobs: " + ", ".join(sorted(failed)))


def occupied_versions(tags: list[dict], releases: list[dict], artifacts: list[dict]) -> list[str]:
    values = [item["ref"].removeprefix("refs/tags/") for item in tags]
    values += [item["tag_name"] for item in releases]
    # Non-validation plan artifacts reserve versions even if a run stops before promotion.
    for item in artifacts:
        match = re.fullmatch(r"release-plan-(v[0-9.]+|none)-(android-v[0-9.]+|none)-[0-9]+-[0-9]+", item["name"])
        if match:
            values += [v for v in match.groups() if v != "none"]
    return values


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--state", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--scope", required=True)
    parser.add_argument("--desktop", default="auto")
    parser.add_argument("--android", default="auto")
    parser.add_argument("--mode", default="approval")
    parser.add_argument("--store-action", default="prepare")
    parser.add_argument("--source", required=True)
    parser.add_argument("--ref", required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--attempt", required=True)
    parser.add_argument("--automatic-enabled", choices=["true", "false"], default="false")
    args = parser.parse_args()
    state = json.loads(args.state.read_text(encoding="utf-8"))
    plan = make_plan(scope=args.scope, desktop=args.desktop, android=args.android, mode=args.mode,
                     store_action=args.store_action, source=args.source, ref=args.ref,
                     run_id=args.run_id, attempt=args.attempt,
                     automatic_enabled=args.automatic_enabled == "true",
                     occupied=occupied_versions(state["tags"], state["releases"], state["artifacts"]))
    args.output.write_text(json.dumps(plan, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
