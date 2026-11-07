"""Publish existing production APKs in a separately signed F-Droid binary repository."""
import argparse
import base64
import hashlib
import html
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
from urllib.parse import urlparse
from urllib.request import urlopen

ROOT = Path(__file__).resolve().parents[2]
MAX_APK_BYTES = 256 * 1024 * 1024
LOCALES = ("en-US", "de-DE", "fr-FR", "es-ES")


def version_from_tag(tag):
    match = re.fullmatch(r"android-v(\d{1,3})\.(\d{1,2})\.(\d{1,3})", tag)
    if not match:
        raise ValueError("Not a stable Android release tag.")
    major, minor, patch = map(int, match.groups())
    if major > 209:
        raise ValueError("Android major version is outside the supported range.")
    return tag.removeprefix("android-v"), major * 10000000 + minor * 100000 + patch * 100 + 99


def validate_settings(settings):
    if settings["applicationId"] != "io.github.legends.otpharbor":
        raise ValueError("Only the production Android application is allowed.")
    if settings["sourceRepository"] != "Legends/otp-harbor":
        raise ValueError("Unexpected APK source repository.")
    url = urlparse(settings["url"])
    if (url.scheme != "https" or not url.hostname or url.username or url.password
            or url.query or url.fragment or not url.path.endswith("/fdroid/repo")):
        raise ValueError("The repository must have an HTTPS F-Droid URL.")
    for key in ("apkCertificateSha256", "indexCertificateSha256"):
        if not re.fullmatch(r"[0-9A-F]{64}", settings[key]):
            raise ValueError("Invalid pinned certificate fingerprint.")
    if settings["apkCertificateSha256"] == settings["indexCertificateSha256"]:
        raise ValueError("The APK and index signing identities must be separate.")
    if not 1 <= settings["retainedVersions"] <= 3:
        raise ValueError("Unsupported retained-version count.")


def select_releases(releases, settings):
    selected = []
    seen_codes = set()
    for release in releases:
        tag = release.get("tag_name", "")
        if release.get("draft") or release.get("prerelease") or not re.fullmatch(r"android-v\d+\.\d+\.\d+", tag):
            continue
        version, code = version_from_tag(tag)
        if code in seen_codes:
            raise ValueError("Duplicate Android version code in published releases.")
        seen_codes.add(code)
        selected.append({"version": version, "code": code, "source": release})
    if not selected:
        raise ValueError("No published production Android APK is available.")
    selected = sorted(selected, key=lambda value: value["code"], reverse=True)[:settings["retainedVersions"]]
    for selected_release in selected:
        version, release = selected_release["version"], selected_release.pop("source")
        tag = release["tag_name"]
        prefix = f'https://github.com/{settings["sourceRepository"]}/releases/download/{tag}/'
        assets = release.get("assets", [])
        apk_name = f"OTP-Harbor-android-universal-{version}.apk"
        required = {}
        for name in (apk_name, "SHA256SUMS"):
            matches = [asset for asset in assets if asset.get("name") == name]
            if len(matches) != 1 or matches[0].get("browser_download_url") != prefix + name:
                raise ValueError("Published release assets are missing, duplicated or outside the trusted release.")
            required[name] = matches[0]["browser_download_url"]
        selected_release.update(apkName=apk_name, assets=required)
    return selected


def download(url, destination, limit):
    with urlopen(url, timeout=120) as response, destination.open("xb") as target:
        final_url = urlparse(response.geturl())
        if final_url.scheme != "https" or final_url.hostname not in ("github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com"):
            raise ValueError("Release download redirected outside GitHub's HTTPS asset hosts.")
        length = 0
        while block := response.read(1024 * 1024):
            length += len(block)
            if length > limit:
                raise ValueError("Release asset exceeds its allowed size.")
            target.write(block)
        if not length:
            raise ValueError("Empty release asset.")


def verify_checksum(apk, checksum_text):
    expected = []
    for line in checksum_text.splitlines():
        match = re.fullmatch(r"([a-fA-F0-9]{64}) [ *](.+)", line)
        if match and match[2] == apk.name:
            expected.append(match[1].lower())
    with apk.open("rb") as source:
        actual = hashlib.file_digest(source, "sha256").hexdigest()
    if expected != [actual]:
        raise ValueError("The APK does not match its single published SHA-256 checksum.")
    return actual


def verify_apk_output(signature_output, badging, release, settings):
    fingerprints = re.findall(r"^Signer #\d+ certificate SHA-256 digest: ([0-9a-fA-F]{64})$", signature_output, re.MULTILINE)
    if [value.upper() for value in fingerprints] != [settings["apkCertificateSha256"]]:
        raise ValueError("APK signing certificate does not match the production pin.")
    package = re.search(r"^package: name='([^']+)' versionCode='(\d+)' versionName='([^']+)'", badging, re.MULTILINE)
    if (not package or package[1] != settings["applicationId"] or int(package[2]) != release["code"]
            or package[3] != release["version"] or re.search(r"^application-debuggable", badging, re.MULTILINE)):
        raise ValueError("APK identity, version or release configuration does not match its tag.")


def command_output(command, cwd=None):
    result = subprocess.run(command, cwd=cwd, capture_output=True, text=True, check=False)
    if result.returncode:
        # Native tools can include configuration details; do not copy their raw output into CI logs.
        raise RuntimeError(f"{Path(command[0]).name} failed with exit code {result.returncode}.")
    return result.stdout


def check_public_files(repo):
    forbidden = {".p12", ".pfx", ".jks", ".keystore", ".key", ".pem", ".py", ".dpapi"}
    for path in repo.rglob("*"):
        if (path.is_symlink() or path.name.startswith(".") or path.suffix.lower() in forbidden
                or path.name.lower().startswith("config.") or path.name.lower() == "keystore-password.txt"):
            raise ValueError("Private or unexpected material was found in the public repository output.")


def render_page(template, settings):
    validate_settings(settings)
    for name, value in (("URL", settings["url"]), ("FINGERPRINT", settings["indexCertificateSha256"])):
        token = "{{FDROID_REPOSITORY_" + name + "}}"
        if token not in template:
            raise ValueError("Repository page is missing a required public setting.")
        template = template.replace(token, html.escape(value, quote=True))
    if "{{FDROID_" in template:
        raise ValueError("Unresolved repository page setting.")
    return template


def build(args):
    settings = json.loads((ROOT / "packaging/fdroid/repository.json").read_text(encoding="utf-8"))
    validate_settings(settings)
    releases = select_releases(json.loads(args.releases_json.read_text(encoding="utf-8-sig")), settings)
    workspace = args.workspace.resolve()
    if workspace == ROOT or ROOT in workspace.parents:
        raise ValueError("The private signing workspace must be outside the checkout.")
    workspace.mkdir(mode=0o700)  # Existing workspaces are rejected; never overwrite an index identity or old output.
    os.chmod(workspace, 0o700)
    keystore = workspace / "repository-index.p12"
    keystore.write_bytes(base64.b64decode(os.environ["FDROID_INDEX_KEYSTORE_BASE64"], validate=True))
    os.chmod(keystore, 0o600)
    password = os.environ.pop("FDROID_INDEX_KEYSTORE_PASSWORD")
    os.environ.pop("FDROID_INDEX_KEYSTORE_BASE64")
    if not password or "\n" in password or "\r" in password:
        raise ValueError("Repository keystore password is invalid.")
    config = {
        "repo_url": settings["url"], "repo_name": settings["name"],
        "repo_description": "Production-signed OTP Harbor releases. Maintained by OTP Harbor; this is an additional repository, separate from the official F-Droid catalog.",
        "repo_icon": "repo-icon.png", "repo_keyalias": settings["keyAlias"],
        "keystore": str(keystore), "keystorepass": password, "keypass": password,
        "sdk_path": os.environ.get("ANDROID_HOME", "/usr/lib/android-sdk"),
        "make_current_version_link": False,
    }
    config_path = workspace / "config.yml"
    config_path.write_text(json.dumps(config), encoding="utf-8")
    os.chmod(config_path, 0o600)
    shutil.copyfile(ROOT / "fastlane/metadata/android/en-US/images/icon.png", workspace / "repo-icon.png")
    repo = workspace / "repo"
    repo.mkdir()
    metadata_dir = workspace / "metadata"
    metadata_dir.mkdir()
    app_id = settings["applicationId"]
    metadata = {
        "Categories": ["Security"], "License": "GPL-3.0-only", "AuthorName": "OTP Harbor contributors",
        "AuthorEmail": "otpharbor.support@proton.me", "WebSite": "https://legends.github.io/otp-harbor/android/",
        "SourceCode": "https://github.com/Legends/otp-harbor", "IssueTracker": "https://github.com/Legends/otp-harbor/issues",
        "Name": "OTP Harbor", "Summary": "Offline, encrypted TOTP authenticator with secure backups",
        "Description": (ROOT / "fastlane/metadata/android/en-US/full_description.txt").read_text(encoding="utf-8").strip(),
        "AllowedAPKSigningKeys": [settings["apkCertificateSha256"].lower()],
        "AutoUpdateMode": "None", "UpdateCheckMode": "None",
        "CurrentVersion": releases[0]["version"], "CurrentVersionCode": releases[0]["code"],
    }
    (metadata_dir / f"{app_id}.yml").write_text(json.dumps(metadata), encoding="utf-8")
    for locale in LOCALES:
        shutil.copytree(ROOT / "fastlane/metadata/android" / locale, metadata_dir / app_id / locale)
    expected_packages = {}
    for release in releases:
        apk = repo / release["apkName"]
        download(release["assets"][apk.name], apk, MAX_APK_BYTES)
        checksums = workspace / f'{release["code"]}-SHA256SUMS'
        download(release["assets"]["SHA256SUMS"], checksums, 65536)
        digest = verify_checksum(apk, checksums.read_text(encoding="utf-8"))
        signatures = command_output([args.apksigner, "verify", "--print-certs", str(apk)])
        badging = command_output([args.aapt, "dump", "badging", str(apk)])
        verify_apk_output(signatures, badging, release, settings)
        expected_packages[release["code"]] = (apk.name, digest)
    command_output(["fdroid", "update", "--use-date-from-apk"], cwd=workspace)
    # Use F-Droid's own cryptographic verifier, anchored to the committed public index certificate.
    from fdroidserver import common, index
    os.chdir(workspace)
    common.read_config()
    entry, _, _ = index.get_index_from_jar(str(repo / "entry.jar"), settings["indexCertificateSha256"])
    index_file = repo / "index-v2.json"
    with index_file.open("rb") as source:
        index_digest = hashlib.file_digest(source, "sha256").hexdigest()
    if entry["index"]["sha256"] != index_digest:
        raise ValueError("Signed entry does not match the generated index.")
    v1, _, _ = index.get_index_from_jar(str(repo / "index-v1.jar"), settings["indexCertificateSha256"], allow_deprecated=True)
    if v1["repo"]["address"] != settings["url"] or set(v1["packages"]) != {app_id}:
        raise ValueError("Generated repository contains unexpected applications or address.")
    packages = {value["versionCode"]: (value["apkName"], value["hash"]) for value in v1["packages"][app_id]}
    if packages != expected_packages:
        raise ValueError("Generated repository packages do not match the verified release APKs.")
    check_public_files(repo)
    print(f'Verified signed repository: {len(packages)} production versions; index certificate {settings["indexCertificateSha256"]}.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--releases-json", type=Path, required=True)
    parser.add_argument("--workspace", type=Path, required=True)
    parser.add_argument("--apksigner", default="apksigner")
    parser.add_argument("--aapt", default="aapt")
    args = parser.parse_args()
    os.umask(0o077)
    try:
        build(args)
    except Exception as error:
        detail = str(error) if type(error) in (ValueError, RuntimeError) else type(error).__name__
        print(f"Repository generation failed: {detail}. No output may be published.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
