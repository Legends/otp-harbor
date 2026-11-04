"""Record every genuine capture with dimensions, provenance and source hash."""
import hashlib
import json
from pathlib import Path

from PIL import Image

root = Path(__file__).resolve().parents[2] / "artifacts/marketing"
manifest_path = root / "manifest.json"
manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
previous = {item["path"]: item for item in manifest.get("captures", [])}
items = []

for capture in sorted((root / "source").rglob("*.png")):
    relative = capture.relative_to(root).as_posix()
    parts = capture.relative_to(root / "source").parts
    if len(parts) != 3 or parts[0] not in ("desktop", "android") or parts[1] not in ("light", "dark"):
        raise SystemExit(f"Unexpected capture path: {relative}")
    with Image.open(capture) as image:
        width, height = image.size
    digest = hashlib.sha256(capture.read_bytes()).hexdigest()
    prior = previous.get(relative, {})
    method = prior.get("method") if prior.get("sha256") == digest else None
    item = {
        "path": relative,
        "platform": parts[0],
        "theme": parts[1],
        "purpose": capture.stem,
        "method": method or "manual capture of the live app",
        "width": width,
        "height": height,
        "sha256": digest,
    }
    if prior.get("sha256") == digest and prior.get("originalSha256"):
        item["originalSha256"] = prior["originalSha256"]
    items.append(item)

manifest["captures"] = items
manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
print(f"Recorded {len(items)} genuine captures.")
