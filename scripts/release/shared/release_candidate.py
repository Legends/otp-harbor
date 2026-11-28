"""Immutable candidate inventory, separate from mutable distribution status."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import xml.etree.ElementTree as ET
import zipfile

from release_plan import make_plan


def sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def safe_file(root: Path, name: str) -> Path:
    if not isinstance(name, str) or "\\" in name or ":" in name or "\x00" in name:
        raise ValueError("Unsafe artifact path.")
    parts = PurePosixPath(name).parts
    if not parts or name.startswith("/") or any(p in (".", "..") for p in name.split("/")):
        raise ValueError("Unsafe artifact path.")
    path = root.joinpath(*parts)
    if not path.resolve().is_relative_to(root.resolve()) or any(p.is_symlink() for p in (path, *path.parents)):
        raise ValueError("Artifact escapes staging or uses a symbolic link.")
    if not path.is_file() or path.stat().st_size == 0:
        raise ValueError("Artifact missing or empty: " + name)
    return path


def validate_plan(plan: dict) -> None:
    expected = make_plan(scope=plan["scope"], desktop=plan["desktopVersion"] or "auto",
                         android=plan["androidVersion"] or "auto", mode=plan["mode"],
                         store_action=plan["storeAction"], source=plan["sourceCommit"], ref=plan["sourceRef"],
                         occupied=[], run_id=plan["workflowRunId"], attempt=plan["workflowRunAttempt"],
                         automatic_enabled=plan["mode"] == "automatic")
    if expected != plan:
        raise ValueError("Candidate release plan is inconsistent.")


def check_msix(root: Path, expected_version: str) -> dict:
    metadata = json.loads(safe_file(root, "store/store-package.json").read_text(encoding="utf-8-sig"))
    package = safe_file(root, "store/" + metadata["package"])
    if metadata.get("schemaVersion") != 1 or metadata.get("distribution") != "microsoft-store-only" or metadata.get("signed") is not False:
        raise ValueError("Unexpected Store submission profile.")
    expected = ("Legends77.OTPHarbor", "CN=84095A7C-6458-436E-ABF2-DC02311E25F9", expected_version, "x64")
    if tuple(metadata.get(k) for k in ("identityName", "publisher", "version", "architecture")) != expected:
        raise ValueError("Store metadata identity/version/architecture mismatch.")
    if sha256(package) != metadata["sha256"]:
        raise ValueError("MSIX hash mismatch.")
    with zipfile.ZipFile(package) as archive:
        identity = ET.fromstring(archive.read("AppxManifest.xml")).find("{*}Identity")
        if identity is None or tuple(identity.get(k) for k in ("Name", "Publisher", "Version", "ProcessorArchitecture")) != expected:
            raise ValueError("MSIX manifest identity/version/architecture mismatch.")
    return metadata


def check_android(root: Path, release_version: str, expected_code: int, certificate: str) -> dict:
    metadata = json.loads(safe_file(root, "android/android-release.json").read_text(encoding="utf-8-sig"))
    package = safe_file(root, "android/" + metadata["fileName"])
    if metadata.get("applicationId") != "io.github.legends.otpharbor" or metadata.get("displayVersion") != release_version:
        raise ValueError("Android package identity/version mismatch.")
    if metadata.get("versionCode") != expected_code:
        raise ValueError("Android versionCode mismatch.")
    if not re.fullmatch(r"[0-9a-fA-F]{64}", certificate) or str(metadata.get("certificateSha256", "")).lower() != certificate.lower():
        raise ValueError("Android certificate mismatch.")
    if metadata["fileName"] != f"OTP-Harbor-android-universal-{release_version}.apk" or sha256(package) != metadata["sha256"]:
        raise ValueError("Android filename/hash mismatch.")
    return metadata


def inventory(root: Path) -> list[dict]:
    return [{"fileName": p.relative_to(root).as_posix(), "bytes": p.stat().st_size, "sha256": sha256(p)}
            for p in sorted(root.rglob("*")) if p.is_file() and safe_file(root, p.relative_to(root).as_posix())]


def validate_test_results(results: dict, source: str) -> None:
    counts = results.get('counts', {})
    if (results.get('sourceCommit') != source or results.get('suite') != 'full-integration'
            or any(type(counts.get(key)) is not int or counts[key] < 0 for key in ('total', 'passed', 'failed', 'skipped'))
            or counts['failed'] != 0 or counts['passed'] <= 0
            or counts['total'] != counts['passed'] + counts['failed'] + counts['skipped']):
        raise ValueError('Complete successful full integration test evidence for this source is required.')


def create_candidate(plan: dict, root: Path, artifacts: list[dict], evidence: dict,
                     store_version: str | None, android_code: int | None, certificate: str, test_results: dict) -> dict:
    validate_plan(plan)
    if any(value != "success" for value in evidence.values()) or not evidence:
        raise ValueError("Mandatory candidate validation evidence is unsuccessful.")
    validate_test_results(test_results, plan['sourceCommit'])
    packages = []
    if plan["desktopVersion"]:
        metadata = check_msix(root, store_version)
        packages += [{"platform": "windows", "architecture": "x64", "identity": metadata["identityName"],
                      "version": store_version, "signing": "unsigned-store-submission", "channel": "microsoft-store"}]
        for filename in (f"OTP-Harbor-linux-x64-{plan['desktopVersion']}.tar.gz", f"otp-harbor_{plan['desktopVersion']}_amd64.deb"):
            safe_file(root, "desktop/" + filename)
        packages += [{"platform": "linux", "architecture": "x64", "version": plan["desktopVersion"],
                      "signing": "ed25519-manifest" if plan["productionEligible"] else "unsigned-validation", "channel": "github"}]
        safe_file(root, "desktop/release-artifacts-v2.json")
        if plan["productionEligible"]:
            for name in ("release-artifacts-v2.json.signature", "appcast-v2.xml", "appcast-v2.xml.signature"):
                safe_file(root, "desktop/" + name)
    if plan["androidVersion"]:
        if plan["productionEligible"]:
            metadata = check_android(root, plan["androidVersion"], android_code, certificate)
            packages += [{"platform": "android", "architecture": "universal", "identity": metadata["applicationId"],
                          "version": metadata["displayVersion"], "versionCode": metadata["versionCode"],
                          "signing": "production-certificate-verified", "certificateSha256": certificate.lower(),
                          "channel": "github-and-project-fdroid"}]
            for name in ("release-artifacts-v2.json", "SHA256SUMS"):
                safe_file(root, "android/" + name)
        else:
            if not list(root.glob("android/*.apk")):
                raise ValueError("Android validation package missing.")
            packages += [{"platform": "android", "architecture": "universal", "signing": "test-signed-validation"}]
    files = inventory(root)
    if {item['platform'] for item in packages} != set(plan['platforms']):
        raise ValueError('A selected platform lacks a reviewed package handler and verification result.')
    if not artifacts or any(not isinstance(a.get("id"), int) or a["id"] <= 0 for a in artifacts):
        raise ValueError("Missing GitHub artifact identifiers.")
    return {"schemaVersion": 1, "plan": plan, "files": files, "packages": packages,
            "artifacts": artifacts, "validation": evidence, "testResults": test_results,
            "toolchain": {"sdk": "10.0.404", "androidWorkload": "10.0.403"}}


def verify_candidate(candidate: dict, root: Path, digest: str, manifest: Path, production: bool = True) -> None:
    if sha256(manifest) != digest or candidate.get("schemaVersion") != 1:
        raise ValueError("Candidate digest/schema mismatch; new approval required.")
    validate_plan(candidate["plan"])
    if production and not candidate["plan"]["productionEligible"]:
        raise ValueError("Validation candidates cannot be promoted.")
    if not candidate.get("validation") or any(v != "success" for v in candidate["validation"].values()):
        raise ValueError("Candidate lacks successful validation evidence.")
    validate_test_results(candidate.get('testResults', {}), candidate['plan']['sourceCommit'])
    if candidate["files"] != inventory(root):
        raise ValueError("Candidate files changed, missing, extra, or corrupted.")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["create", "verify"])
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--digest")
    parser.add_argument("--plan", type=Path)
    parser.add_argument("--artifacts", type=Path)
    parser.add_argument("--evidence", type=Path)
    parser.add_argument("--test-evidence", type=Path)
    parser.add_argument("--store-version")
    parser.add_argument("--android-code", type=int)
    parser.add_argument("--certificate", default="")
    parser.add_argument("--validation", action="store_true")
    args = parser.parse_args()
    if args.action == "verify":
        verify_candidate(json.loads(args.manifest.read_text(encoding="utf-8-sig")), args.root, args.digest, args.manifest, not args.validation)
    else:
        result = create_candidate(json.loads(args.plan.read_text(encoding="utf-8-sig")), args.root, json.loads(args.artifacts.read_text(encoding="utf-8-sig")),
                                  json.loads(args.evidence.read_text(encoding="utf-8-sig")), args.store_version, args.android_code, args.certificate,
                                  json.loads(args.test_evidence.read_text(encoding='utf-8-sig')))
        args.manifest.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(sha256(args.manifest))


if __name__ == "__main__":
    main()
