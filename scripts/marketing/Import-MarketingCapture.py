"""Ingest a user-captured image of the live app into the canonical source tree."""
import argparse
import hashlib
import json
import re
from pathlib import Path

from PIL import Image, ImageStat


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("--platform", choices=("desktop", "android"), required=True)
    parser.add_argument("--theme", choices=("light", "dark"), required=True)
    parser.add_argument("--screen", required=True)
    parser.add_argument("--replace", action="store_true")
    args = parser.parse_args()
    if not re.fullmatch(r"[a-z0-9]+(?:-[a-z0-9]+)*", args.screen):
        parser.error("--screen must use lowercase words separated by hyphens")
    if not args.source.is_file():
        parser.error(f"Capture does not exist: {args.source}")

    root = Path(__file__).resolve().parents[2] / "artifacts/marketing"
    destination = root / "source" / args.platform / args.theme / f"{args.screen}.png"
    if args.source.resolve() == destination.resolve():
        parser.error("Save the original outside the canonical source tree, then import it")
    if destination.exists() and not args.replace:
        parser.error(f"Capture already exists: {destination}; use --replace to overwrite it")

    source_bytes = args.source.read_bytes()
    original_hash = hashlib.sha256(source_bytes).hexdigest()
    with Image.open(args.source) as image:
        image.load()
        width, height = image.size
        if width < 320 or height < 480:
            parser.error(f"Capture dimensions are too small: {width}x{height}")
        if max(ImageStat.Stat(image.convert("RGB")).stddev) <= 8:
            parser.error("Capture appears blank or screen protected")
        destination.parent.mkdir(parents=True, exist_ok=True)
        # Re-encode losslessly to strip EXIF, paths and other source metadata.
        image.convert("RGBA" if image.mode in ("RGBA", "LA") else "RGB").save(destination)

    manifest_path = root / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    relative = destination.relative_to(root).as_posix()
    digest = hashlib.sha256(destination.read_bytes()).hexdigest()
    record = {
        "path": relative,
        "platform": args.platform,
        "theme": args.theme,
        "purpose": args.screen,
        "method": "manual capture of the live app",
        "width": width,
        "height": height,
        "sha256": digest,
        "originalSha256": original_hash,
    }
    manifest["captures"] = sorted(
        [item for item in manifest.get("captures", []) if item["path"] != relative] + [record],
        key=lambda item: item["path"],
    )
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Imported live-app capture: {destination} ({width}x{height})")


if __name__ == "__main__":
    main()
