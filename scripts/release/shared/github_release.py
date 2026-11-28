"""Publish verified candidates without replacing tags or existing release assets."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile

from release_candidate import safe_file, sha256, verify_candidate
from release_plan import stable_tag


class Conflict(RuntimeError):
    pass


class GitHub:
    def __init__(self, repository):
        if repository != "Legends/otp-harbor":
            raise Conflict("Unexpected production repository.")
        self.repository = repository

    def api(self, method, path, body=None, missing=False):
        args = ["gh", "api", "--method", method, f"repos/{self.repository}/{path}"]
        with tempfile.TemporaryDirectory(prefix="otp-github-request-") as directory:
            if body is not None:
                request = Path(directory) / "request.json"
                request.write_text(json.dumps(body), encoding="utf-8")
                args += ["--input", str(request)]
            result = subprocess.run(args, capture_output=True, text=True)
        if result.returncode:
            if missing and "HTTP 404" in result.stderr:
                return None
            raise Conflict("GitHub request failed; reconcile the tag, release, and assets before retrying.")
        return json.loads(result.stdout) if result.stdout else None

    def tagsource(self, tag):
        ref = self.api("GET", "git/ref/tags/" + tag, missing=True)
        if ref is None:
            return None
        obj = ref["object"]
        for _ in range(5):
            if obj["type"] == "commit":
                return obj["sha"]
            if obj["type"] != "tag":
                break
            obj = self.api("GET", "git/tags/" + obj["sha"])["object"]
        raise Conflict("Unresolvable release tag.")

    def upload(self, release_id, path):
        result = subprocess.run(["gh", "release", "upload", str(self.tag), str(path), "--repo", self.repository], capture_output=True, text=True)
        # No --clobber. An interrupted successful upload is discovered on retry.
        if result.returncode:
            raise Conflict("Asset upload did not confirm success; retry with the original candidate.")

    def digest(self, asset):
        digest = asset.get("digest")
        if digest and digest.startswith("sha256:"):
            return digest.removeprefix("sha256:")
        result = subprocess.run(["gh", "api", f"repos/{self.repository}/releases/assets/{asset['id']}", "-H", "Accept: application/octet-stream"], capture_output=True)
        if result.returncode:
            raise Conflict("Could not independently verify existing asset bytes.")
        return hashlib.sha256(result.stdout).hexdigest()


def publish(github, tag, source, files, body, name):
    family, number = stable_tag(tag)
    github.tag = tag
    existing_source = github.tagsource(tag)
    if existing_source is None:
        try:
            github.api("POST", "git/refs", {"ref": "refs/tags/" + tag, "sha": source})
        except Conflict:
            # A response may be lost after successful creation. Read before deciding.
            if github.tagsource(tag) != source:
                raise
    if github.tagsource(tag) != source:
        raise Conflict("Existing tag points to another source; it will never be moved.")
    release = github.api("GET", "releases/tags/" + tag, missing=True)
    if release is None:
        try:
            release = github.api("POST", "releases", {"tag_name": tag, "target_commitish": source,
                                 "name": name, "body": body, "draft": True, "prerelease": False, "make_latest": "false"})
        except Conflict:
            release = github.api("GET", "releases/tags/" + tag, missing=True)
            if release is None:
                raise
    if release.get("prerelease") or release.get("tag_name") != tag:
        raise Conflict("Existing release identity conflicts.")
    # Includes candidate fingerprint in the body. Never adopt an unrelated draft.
    if release.get("body") != body:
        raise Conflict("Existing release belongs to another candidate or release-note revision.")
    expected = {p.name: p for p in files}
    if len(expected) != len(files):
        raise Conflict("Duplicate asset names.")

    def inspect(assets):
        seen = set()
        for asset in assets:
            name = asset["name"]
            if name in seen or name not in expected:
                raise Conflict("Unexpected or duplicate release asset.")
            seen.add(name)
            path = expected[name]
            if asset["size"] != path.stat().st_size or github.digest(asset) != sha256(path):
                raise Conflict("Existing asset differs; published assets are immutable.")
        return seen

    # GET asset collection explicitly: the embedded release asset array is bounded.
    assets = []
    page = 1
    while True:
        batch = github.api("GET", f"releases/{release['id']}/assets?per_page=100&page={page}")
        assets.extend(batch)
        if len(batch) < 100:
            break
        page += 1
    present = inspect(assets)
    if not release["draft"]:
        if present != set(expected):
            raise Conflict("Published release has missing or unexpected assets; no mutation allowed.")
        return release["html_url"]
    for filename, path in expected.items():
        if filename not in present:
            github.upload(release["id"], path)
    actual = github.api("GET", f"releases/{release['id']}/assets?per_page=100")
    if inspect(actual) != set(expected):
        raise Conflict("Draft asset verification incomplete.")
    make_latest = "false"
    if family == "desktop":
        published_versions = []
        page = 1
        while True:
            batch = github.api('GET', f'releases?per_page=100&page={page}')
            for item in batch:
                if item['draft'] or item['prerelease']:
                    continue
                try:
                    item_family, item_version = stable_tag(item['tag_name'])
                except ValueError:
                    continue
                if item_family == 'desktop':
                    published_versions.append(item_version)
            if len(batch) < 100:
                break
            page += 1
        if number >= max(published_versions, default=number):
            make_latest = 'true'
    result = github.api("PATCH", f"releases/{release['id']}", {"draft": False, "prerelease": False, "make_latest": make_latest})
    return result["html_url"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidate", type=Path, required=True)
    parser.add_argument("--digest", required=True)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--family", choices=["desktop", "android"], required=True)
    parser.add_argument("--notes", type=Path, required=True)
    parser.add_argument("--status", type=Path, required=True)
    args = parser.parse_args()
    candidate = json.loads(args.candidate.read_text(encoding="utf-8-sig"))
    verify_candidate(candidate, args.root, args.digest, args.candidate)
    plan = candidate["plan"]
    number = plan["desktopVersion" if args.family == "desktop" else "androidVersion"]
    if number is None:
        raise Conflict("Unselected release family.")
    tag = ("v" if args.family == "desktop" else "android-v") + number
    filenames = ([f"OTP-Harbor-linux-x64-{number}.tar.gz", f"otp-harbor_{number}_amd64.deb", "release-artifacts-v2.json", "release-artifacts-v2.json.signature", "appcast-v2.xml", "appcast-v2.xml.signature"]
                 if args.family == "desktop" else [f"OTP-Harbor-android-universal-{number}.apk", "android-release.json", "release-artifacts-v2.json", "SHA256SUMS"])
    files = [safe_file(args.root, args.family + "/" + name) for name in filenames]
    notes = args.notes.read_text(encoding="utf-8").strip()
    if not notes:
        raise Conflict("Reviewed release notes are required.")
    body = notes + f"\n\nCandidate `{plan['candidateId']}` · source `{plan['sourceCommit']}` · SHA-256 `{args.digest}`\n"
    github = GitHub(os.environ["GITHUB_REPOSITORY"])
    url = publish(github, tag, plan["sourceCommit"], files, body, "OTP Harbor " + ("Android " if args.family == "android" else "") + number)
    args.status.write_text(json.dumps({"family": args.family, "state": "published", "url": url, "candidateSha256": args.digest}) + "\n")
    print(url)


if __name__ == "__main__":
    main()
