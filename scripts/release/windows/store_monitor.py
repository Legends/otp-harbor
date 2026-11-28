"""Discover retained non-secret pending Store records; never access Store credentials."""
import argparse
import io
import json
from pathlib import Path
import re
import subprocess
import zipfile

REPOSITORY = "Legends/otp-harbor"
TRUSTED_WORKFLOWS = {".github/workflows/release.yml", ".github/workflows/release-recovery.yml", ".github/workflows/release-reconciliation.yml"}


def api(path, binary=False):
    result = subprocess.run(["gh", "api", f"repos/{REPOSITORY}/{path}"], capture_output=True)
    if result.returncode:
        raise RuntimeError("Store record discovery failed; no mutation performed.")
    return result.stdout if binary else json.loads(result.stdout)


def pending_records(artifacts, fetch_run, fetch_bytes, find_candidate):
    selected = {}
    # Only the latest known record for a candidate is authoritative. Published or
    # failed terminal records suppress stale pending records from earlier runs.
    for artifact in sorted(artifacts, key=lambda a: a["updated_at"], reverse=True):
        if artifact["expired"]:
            continue
        run_id = artifact["workflow_run"]["id"]
        run = fetch_run(run_id)
        if run["head_branch"] != "master" or run["path"] not in TRUSTED_WORKFLOWS:
            continue
        with zipfile.ZipFile(io.BytesIO(fetch_bytes(artifact["id"]))) as archive:
            if archive.namelist() != ["store-state.json"] or archive.getinfo("store-state.json").file_size > 65536:
                raise ValueError("Invalid Store reconciliation record archive.")
            state = json.loads(archive.read("store-state.json"))
        candidate_id = state.get("candidateId", "")
        if not re.fullmatch(r"[1-9][0-9]*-[1-9][0-9]*", candidate_id) or not re.fullmatch(r"[0-9a-f]{64}", state.get("candidateSha256", "")):
            raise ValueError("Invalid Store record identity.")
        if candidate_id in selected:
            continue
        selected[candidate_id] = None
        if state["state"] in ("published", "failed", "certification-failed") or not state.get("submissionId"):
            continue
        original_run = candidate_id.split("-")[0]
        candidate_artifact = find_candidate(original_run, "release-candidate-" + candidate_id)
        selected[candidate_id] = dict(run_id=original_run, artifact_id=str(candidate_artifact["id"]),
                                     candidate_sha256=state["candidateSha256"], state_artifact_id=str(artifact["id"]), state_run_id=str(run_id))
    result = [item for item in selected.values() if item]
    if len(result) > 1:
        raise ValueError("Multiple pending records for one Store product require targeted store-status reconciliation; none were silently dropped.")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    artifacts = []
    page = 1
    while True:
        batch = api(f"actions/artifacts?name=store-state&per_page=100&page={page}")["artifacts"]
        artifacts.extend(batch)
        if len(batch) < 100:
            break
        page += 1

    def find_candidate(run_id, name):
        batch = api(f"actions/runs/{run_id}/artifacts?per_page=100")["artifacts"]
        matches = [item for item in batch if item["name"] == name and not item["expired"]]
        if len(matches) != 1:
            raise ValueError("Original approved candidate unavailable; Store reconciliation requires manual recovery.")
        return matches[0]

    records = pending_records(artifacts, lambda run_id: api(f"actions/runs/{run_id}"),
                              lambda artifact_id: api(f"actions/artifacts/{artifact_id}/zip", binary=True), find_candidate)
    args.output.write_text(json.dumps({"include": records}), encoding="utf-8")
    print(f"{len(records)} retained Store submission(s) need bounded reconciliation.")


if __name__ == "__main__":
    main()
