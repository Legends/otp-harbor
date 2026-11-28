"""Non-destructive MSIX submission using Microsoft's versioned submission API.

API reference: https://learn.microsoft.com/windows/uwp/monetize/manage-app-submissions
No delete operation exists here. POST operations are never blindly retried.
"""
from __future__ import annotations

import argparse
import base64
import copy
from datetime import datetime, timezone
from email.utils import parsedate_to_datetime
import hashlib
from http.client import HTTPException
import json
import os
from pathlib import Path
import random
import re
import sys
import tempfile
import time
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode, urlparse
from urllib.request import HTTPRedirectHandler, Request, build_opener
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "shared"))
from release_candidate import check_msix, safe_file, sha256, verify_candidate

API = "https://manage.devcenter.microsoft.com/v1.0/my/applications/"
PRODUCT = "9P31KH5L924P"
STATES = {
    "PendingCommit": "prepared", "CommitStarted": "submitted", "PreProcessing": "certification-pending",
    "Certification": "certification-pending", "CertificationFailed": "certification-failed",
    "PendingPublication": "awaiting-publication", "Publishing": "awaiting-publication",
    "Release": "awaiting-publication", "Published": "published", "CommitFailed": "failed",
    "PreProcessingFailed": "failed", "PublishFailed": "failed", "ReleaseFailed": "failed", "Canceled": "failed",
}


class StoreError(RuntimeError):
    pass


class UncertainMutation(StoreError):
    pass


class NoRedirects(HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        raise StoreError("Unexpected redirect rejected.")


def retry_delay(headers, attempt: int) -> float:
    value = headers.get("Retry-After", "")
    if value:
        try:
            delay = float(value)
        except ValueError:
            delay = (parsedate_to_datetime(value) - datetime.now(timezone.utc)).total_seconds()
        if delay > 60:
            raise StoreError("Retry-After exceeds this bounded operation; retry the workflow later.")
        return max(0, delay) + random.uniform(0, 0.25)
    return min(2 ** attempt, 30) + random.uniform(0, 0.5)


class Http:
    def __init__(self, sleep=time.sleep):
        self.sleep = sleep
        self.opener = build_opener(NoRedirects)

    def request(self, method, url, headers=None, data=None, retry=True):
        if urlparse(url).scheme != "https":
            raise StoreError("HTTPS is required.")
        for attempt in range(4):
            try:
                with self.opener.open(Request(url, method=method, headers=headers or {}, data=data), timeout=60) as response:
                    return response.read(), response.headers
            except HTTPError as error:
                if method == "POST" and error.code >= 500:
                    raise UncertainMutation("Store mutation outcome is unknown; reconcile remote state before retrying.") from None
                if not retry or error.code not in (429, 500, 502, 503, 504) or attempt == 3:
                    # Response bodies and URLs can contain tokens or SAS credentials.
                    raise StoreError(f"Remote operation failed with HTTP {error.code}.") from None
                if method == "POST":
                    raise UncertainMutation("Store mutation was not retried; inspect remote state.") from None
                self.sleep(retry_delay(error.headers, attempt))
            except (URLError, TimeoutError, OSError, HTTPException):
                if method == "POST":
                    raise UncertainMutation("Store mutation response interrupted; reconcile remote state.") from None
                if not retry or attempt == 3:
                    raise StoreError("Remote operation unavailable after bounded retries.") from None
                self.sleep(retry_delay({}, attempt))


class Store:
    def __init__(self, tenant, client_id, client_secret, product=PRODUCT, http=None):
        if not re.fullmatch(r"[0-9a-fA-F-]{36}", tenant or "") or not client_id or not client_secret:
            raise StoreError("Protected Store authentication configuration is incomplete.")
        if product != PRODUCT:
            raise StoreError("Unexpected Store product.")
        self.tenant, self.client_id, self.secret, self.product = tenant, client_id, client_secret, product
        self.http, self.token = http or Http(), None

    def authenticate(self):
        data = urlencode(dict(grant_type="client_credentials", client_id=self.client_id, client_secret=self.secret,
                              resource="https://manage.devcenter.microsoft.com")).encode()
        response, _ = self.http.request("POST", f"https://login.microsoftonline.com/{self.tenant}/oauth2/token",
                                        {"Content-Type": "application/x-www-form-urlencoded"}, data, retry=False)
        self.token = json.loads(response)["access_token"]

    def api(self, method, suffix="", body=None):
        if self.token is None:
            self.authenticate()
        url = API + self.product + suffix
        headers = {"Authorization": "Bearer " + self.token, "Content-Type": "application/json"}
        data = None if body is None else json.dumps(body, ensure_ascii=False).encode("utf-8")
        try:
            raw, _ = self.http.request(method, url, headers, data, retry=method in ("GET", "PUT"))
        except StoreError as error:
            if str(error) == "Remote operation failed with HTTP 401." and method == "GET":
                self.authenticate()
                headers["Authorization"] = "Bearer " + self.token
                raw, _ = self.http.request(method, url, headers, data)
            else:
                raise
        try:
            return json.loads(raw) if raw else {}
        except json.JSONDecodeError:
            if method == 'POST':
                raise UncertainMutation('Store mutation returned an invalid response; reconcile remote state.') from None
            raise StoreError('Store returned an invalid response.') from None

    def upload(self, url, archive):
        parsed = urlparse(url)
        if parsed.scheme != "https" or not (parsed.hostname or "").endswith(".blob.core.windows.net") or parsed.username or parsed.fragment:
            raise StoreError("Unexpected Store upload destination.")
        data = archive.read_bytes()
        checksum = base64.b64encode(hashlib.md5(data, usedforsecurity=False).digest()).decode()
        self.http.request("PUT", url, {"x-ms-blob-type": "BlockBlob", "x-ms-version": "2021-12-02",
                                      "Content-Type": "application/zip", "Content-MD5": checksum}, data)
        _, headers = self.http.request("HEAD", url)
        if int(headers.get("Content-Length", "-1")) != len(data) or headers.get("Content-MD5") != checksum:
            raise StoreError("Store upload verification failed.")


def package_version(value):
    if not isinstance(value, str) or not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+\.0", value):
        raise StoreError("Invalid Store package version.")
    parts = tuple(map(int, value.split(".")))
    if parts[0] < 1 or any(v > 65535 for v in parts):
        raise StoreError("Store version constraints violated.")
    return parts


def save_state(path, state):
    # Deliberately allowlist fields; never persist API bodies, access tokens or upload URLs.
    allowed = ("schemaVersion", "candidateSha256", "candidateId", "sourceCommit", "productId", "packageVersion",
               "packageSha256", "submissionId", "state", "remoteStatus", "creationUncertain", "commitUncertain", "submittedAt", "publishedAt", "updatedAt")
    state["updatedAt"] = datetime.now(timezone.utc).isoformat()
    path.write_text(json.dumps({k: state[k] for k in allowed if k in state}, indent=2) + "\n", encoding="utf-8")


def reconcile(store, state):
    identifier = state.get("submissionId", "")
    if not re.fullmatch(r"[0-9]+", identifier):
        raise StoreError("No verified submission ID available for reconciliation.")
    remote = store.api("GET", "/submissions/" + identifier)
    state["remoteStatus"] = remote.get("status", "Unknown")
    state["state"] = STATES.get(state["remoteStatus"], "unknown")
    if state["state"] == "published":
        packages = [p for p in remote.get("applicationPackages", []) if p.get("fileStatus") == "Uploaded"]
        if not any(p.get("version") == state["packageVersion"] and str(p.get("architecture", "")).lower() == "x64" for p in packages):
            raise StoreError("Published submission does not contain the expected package version.")
        state.setdefault("publishedAt", datetime.now(timezone.utc).isoformat())
    return state


def submit(store, package, metadata, state, state_path):
    app = store.api("GET")
    if (app.get("id"), app.get("packageIdentityName"), app.get("publisherName")) != (PRODUCT, metadata["identityName"], metadata["publisher"]):
        raise StoreError("Partner Center application identity mismatch.")
    pending = (app.get("pendingApplicationSubmission") or {}).get("id")
    published_id = (app.get("lastPublishedApplicationSubmission") or {}).get("id")
    if not published_id:
        raise StoreError("Initial Store publication must be completed in Partner Center.")
    published = store.api("GET", "/submissions/" + published_id)
    uploaded = [p for p in published.get("applicationPackages", []) if p.get("fileStatus") == "Uploaded"]
    if not uploaded:
        raise StoreError("Published architecture/version coverage is unavailable.")
    current = max(package_version(p["version"]) for p in uploaded)
    if package_version(metadata["version"]) <= current:
        if state.get("submissionId") == published_id and metadata["version"] == state["packageVersion"]:
            return reconcile(store, state)
        raise StoreError("Candidate does not advance the published Store package version.")
    marker = f"OTP Harbor candidate {state['candidateId']} sha256 {state['candidateSha256']}"
    if pending:
        if pending != state.get("submissionId") or state.get("creationUncertain"):
            raise StoreError("Unrelated or uncertain pending submission protected; no changes made.")
        submission = store.api("GET", "/submissions/" + pending)
        if marker not in submission.get("notesForCertification", ""):
            raise StoreError("Submission ownership marker missing; manual reconciliation required.")
        if submission.get("status") != "PendingCommit":
            return reconcile(store, state)
        if state.get("commitUncertain"):
            raise StoreError("Commit outcome remains uncertain; reconcile without repeating commit.")
    else:
        if state.get("submissionId") or state.get("creationUncertain"):
            raise StoreError("Original submission disappeared; refusing to create a replacement.")
        try:
            submission = store.api("POST", "/submissions")
        except UncertainMutation:
            state["creationUncertain"], state["state"] = True, "unknown"
            fresh = store.api("GET")
            state["submissionId"] = (fresh.get("pendingApplicationSubmission") or {}).get("id")
            save_state(state_path, state)
            raise
        pending = submission["id"]
        state["submissionId"], state["state"] = pending, "prepared"
        save_state(state_path, state)
        if submission.get("status") != "PendingCommit":
            raise StoreError("New submission is not editable; reconcile without changing it.")
    if not re.fullmatch(r"[0-9]+", pending):
        raise StoreError("Invalid submission identity.")
    # API returns a clone of the published submission. Preserve all listing/settings
    # fields and retain packages for other architectures. Replace only x64 packages.
    changed = copy.deepcopy(submission)
    if marker not in changed.get("notesForCertification", ""):
        changed["notesForCertification"] = changed.get("notesForCertification", "") + "\n" + marker
    packages = changed.setdefault("applicationPackages", [])
    for item in packages:
        if str(item.get("architecture", "")).lower() == "x64":
            item["fileStatus"] = "PendingDelete"
    packages[:] = [p for p in packages if p.get("fileName") != package.name]
    packages.append({"fileName": package.name, "fileStatus": "PendingUpload", "minimumDirectXVersion": "None", "minimumSystemRam": "None"})
    store.api("PUT", "/submissions/" + pending, changed)
    with tempfile.TemporaryDirectory(prefix="otp-store-upload-") as directory:
        archive = Path(directory) / "submission.zip"
        with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as output:
            output.write(package, package.name)
        store.upload(submission["fileUploadUrl"], archive)
    state["state"] = "uploaded"
    save_state(state_path, state)
    verified = store.api("GET", "/submissions/" + pending)
    remote_packages = verified.get("applicationPackages", [])
    if len([p for p in remote_packages if p.get("fileName") == package.name and p.get("fileStatus") in ("PendingUpload", "Uploaded")]) != 1:
        raise StoreError("Approved package entry missing or ambiguous; commit blocked.")
    for old_package in submission.get("applicationPackages", []):
        if str(old_package.get("architecture", "")).lower() != "x64" and old_package.get("fileStatus") == "Uploaded":
            if not any(p.get("id") == old_package.get("id") and p.get("fileName") == old_package.get("fileName") and p.get("fileStatus") == "Uploaded" for p in remote_packages):
                raise StoreError("Existing package architecture coverage changed; commit blocked.")
    # Prior to commit the API may keep PendingUpload until ingestion. The uploaded
    # blob's MD5/length is checked independently; all unrelated fields must survive.
    for key in changed:
        if key not in ("status", "statusDetails", "fileUploadUrl", "applicationPackages") and verified.get(key) != changed[key]:
            raise StoreError("Store listing/settings changed unexpectedly; commit blocked.")
    try:
        store.api("POST", "/submissions/" + pending + "/commit")
    except UncertainMutation:
        state["commitUncertain"] = True
        reconcile(store, state)
        save_state(state_path, state)
        raise
    state["state"] = "submitted"
    state.setdefault("submittedAt", datetime.now(timezone.utc).isoformat())
    save_state(state_path, state)
    return reconcile(store, state)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["submit", "reconcile"])
    parser.add_argument("--candidate", type=Path, required=True)
    parser.add_argument("--digest", required=True)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--state", type=Path, required=True)
    args = parser.parse_args()
    manifest = json.loads(args.candidate.read_text(encoding="utf-8-sig"))
    verify_candidate(manifest, args.root, args.digest, args.candidate)
    plan = manifest["plan"]
    if "windows" not in plan["platforms"] or plan["storeAction"] != "submit":
        raise StoreError("Approved candidate does not request Store submission.")
    windows = next(p for p in manifest["packages"] if p["platform"] == "windows")
    metadata = check_msix(args.root, windows["version"])
    identity = dict(schemaVersion=1, candidateSha256=args.digest, candidateId=plan["candidateId"], sourceCommit=plan["sourceCommit"],
                    productId=PRODUCT, packageVersion=metadata["version"], packageSha256=metadata["sha256"])
    if args.state.exists():
        state = json.loads(args.state.read_text(encoding="utf-8-sig"))
        if any(state.get(k) != v for k, v in identity.items()):
            raise StoreError("Recovery identity differs from original candidate.")
    else:
        state = identity | {"state": "prepared"}
    store = Store(os.environ.get("STORE_TENANT_ID"), os.environ.get("STORE_CLIENT_ID"), os.environ.get("STORE_CLIENT_SECRET"))
    try:
        if args.action == "submit":
            submit(store, safe_file(args.root, "store/" + metadata["package"]), metadata, state, args.state)
        else:
            reconcile(store, state)
    finally:
        save_state(args.state, state)
    print(f"Store submission {state.get('submissionId', 'unknown')}: {state['state']}. Upload is not publication.")
    if state["state"] in ("failed", "certification-failed", "unknown"):
        raise StoreError("Store reconciliation requires operator attention.")


if __name__ == "__main__":
    try:
        main()
    except (StoreError, ValueError, KeyError, OSError) as error:
        # Intentionally exclude arbitrary external exception text/response bodies.
        print(f"Store operation stopped ({type(error).__name__}); inspect retained non-secret state and recovery guide.", file=sys.stderr)
        sys.exit(1)
