"""Make explicitly labeled promotional composites from exact captured pixels."""

import hashlib
import io
import json
from pathlib import Path

import segno
import zxingcpp
from PIL import Image, ImageDraw
from screenshot_cleanup import clean_desktop


ROOT = Path(__file__).resolve().parents[2] / "artifacts/marketing"
URL = "https://legends.github.io/otp-harbor/"


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def record(path: Path, kind: str, source: Path, **details: object) -> dict:
    return {
        "path": path.relative_to(ROOT).as_posix(),
        "sha256": digest(path),
        "sourceSha256": [digest(source)],
        "kind": kind,
        "publicationUse": "promotional composite; not a genuine Store screenshot",
        **details,
    }


def qr_image(scale: int) -> Image.Image:
    qr = segno.make(URL, error="h", micro=False)
    buffer = io.BytesIO()
    qr.save(buffer, kind="png", scale=scale, border=4, dark="#101c31", light="#ffffff")
    buffer.seek(0)
    return Image.open(buffer).convert("RGB")


def scanner(platform: str, theme: str) -> dict:
    source = ROOT / "source" / platform / theme / "scanner.png"
    if not source.is_file():
        raise FileNotFoundError(source)
    with Image.open(source) as original:
        image, crop = (clean_desktop(original) if platform == "desktop"
                       else (original.convert("RGB"), None))
    expected = (1220, 2712) if platform == "android" else (1400, 1050)
    if (image.size != ((1394, 1044) if platform == "desktop" else expected)
        or (platform == "desktop" and crop != (3, 3, 1397, 1047))):
        raise ValueError(f"Unexpected {platform} scanner dimensions: {image.size}; update the QR position deliberately")
    qr = qr_image(12 if platform == "android" else 6)
    if platform == "desktop":
        draw = ImageDraw.Draw(image)
        art_bounds = (372, 444, 1023, 810)
        draw.rectangle((372, 444, 1022, 809),
                       fill=("#EAF4FC" if theme == "light" else "#0D223D"))
        draw.rounded_rectangle((528, 453, 870, 806), radius=28,
                               fill=("#D2E6F8" if theme == "light" else "#061122"))
        draw.rounded_rectangle((522, 448, 873, 807), radius=28,
                               fill=("#F8FCFF" if theme == "light" else "#101F38"),
                               outline=("#5AA8D4" if theme == "light" else "#64C7F5"), width=3)
        draw.rounded_rectangle((541, 460, 854, 795), radius=13, fill="#FFFFFF")
        origin = (563, 493)
    else:
        center = (610, 1359)
        origin = (center[0] - qr.width // 2, center[1] - qr.height // 2)
    image.paste(qr, origin)
    result = zxingcpp.read_barcode(image)
    if result is None or result.text != URL:
        raise ValueError(f"Generated {platform} {theme} QR does not decode to {URL}")
    target = ROOT / "composites" / platform / f"scanner-website-qr-{theme}.png"
    target.parent.mkdir(parents=True, exist_ok=True)
    image.save(target, optimize=True)
    use = ("review only: source UI names a third-party authenticator" if platform == "desktop"
           else "promotional composite; not a genuine Store screenshot")
    return record(target, "designed-scanner-qr", source, platform=platform, theme=theme,
                  qrPayload=URL, qrBounds=[*origin, qr.width, qr.height],
                  **({"sourceCrop": list(crop), "artBounds": list(art_bounds)}
                     if platform == "desktop" else {}), publicationUse=use)


PROMPT_BOUNDS = {
    "light": (751, 264, 1888, 1451),
    "dark": (762, 325, 1898, 1511),
}


def biometric(theme: str) -> dict:
    source = ROOT / "source" / "desktop" / theme / "biometric-prompt.png"
    if not source.is_file():
        raise FileNotFoundError(source)
    with Image.open(source) as original:
        image = original.convert("RGBA")
    expected = (1920, 1483) if theme == "light" else (1925, 1544)
    if image.size != expected:
        raise ValueError(f"Unexpected {theme} biometric dimensions: {image.size}; update cutout bounds deliberately")
    prompt = PROMPT_BOUNDS[theme]
    image = image.crop(prompt)
    mask = Image.new("L", image.size)
    draw = ImageDraw.Draw(mask)
    draw.rounded_rectangle((0, 0, image.width - 1, image.height - 1), radius=22, fill=255)
    image.putalpha(mask)
    target = ROOT / "composites" / "desktop" / f"biometric-prompt-{theme}-transparent.png"
    target.parent.mkdir(parents=True, exist_ok=True)
    image.save(target, optimize=True)
    with Image.open(target) as saved:
        if saved.getpixel((0, 0))[3] != 0 or saved.getpixel((100, 100))[3] != 255:
            raise ValueError(f"Biometric cutout transparency failed: {target}")
    return record(target, "transparent-biometric-cutout", source, platform="desktop", theme=theme,
                  promptBounds=list(prompt),
                  publicationUse="review only: Windows Security branding conflicts with fictional-only marketing policy")


def main() -> None:
    entries = [scanner(platform, theme) for platform in ("android", "desktop") for theme in ("light", "dark")]
    entries += [biometric(theme) for theme in ("light", "dark")]
    manifest_path = ROOT / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    paths = {entry["path"] for entry in entries}
    manifest["generated"] = sorted(
        [entry for entry in manifest.get("generated", []) if entry["path"] not in paths] + entries,
        key=lambda entry: entry["path"],
    )
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print("Created four scannable QR composites and two exact-pixel transparent cutouts.")


if __name__ == "__main__":
    main()
