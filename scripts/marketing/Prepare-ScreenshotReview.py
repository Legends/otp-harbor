"""Stage lossless screenshot cleanup candidates without changing canonical assets."""

import hashlib
import html
import io
import json
import shutil
from pathlib import Path

import segno
import zxingcpp
from PIL import Image, ImageDraw
from screenshot_cleanup import clean_android, clean_desktop

REPO = Path(__file__).resolve().parents[2]
ROOT = REPO / 'artifacts/marketing'
SOURCE = ROOT / 'source'
REVIEW = ROOT / 'staging/screenshot-review'
LOCAL_SITE = ROOT / 'staging/local-site'
WEBSITE = 'https://legends.github.io/otp-harbor/'
SCANNER_INPUT = Path(r'C:\Users\Admin\Pictures\Screenpresso\scanner-banana.png')


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def corner_atlas(image, destination):
    """Magnify only diagnostic thumbnails; never resize the candidate capture."""
    image = image.convert('RGBA')
    edge = 48
    tile = 192
    background = Image.new('RGBA', (tile * 2, tile * 2), '#27405e')
    corners = (
        (0, 0, edge, edge),
        (image.width - edge, 0, image.width, edge),
        (0, image.height - edge, edge, image.height),
        (image.width - edge, image.height - edge, image.width, image.height),
    )
    for index, bounds in enumerate(corners):
        cropped = image.crop(bounds).resize((tile, tile), Image.Resampling.NEAREST)
        background.alpha_composite(cropped, ((index % 2) * tile, (index // 2) * tile))
    destination.parent.mkdir(parents=True, exist_ok=True)
    background.save(destination)


def safe_scanner_image():
    if not SCANNER_INPUT.is_file():
        return None
    with Image.open(SCANNER_INPUT) as original:
        image = original.convert('RGB')
    if image.size != (1186, 867):
        raise ValueError('Scanner source dimensions changed; review redaction bounds before using it.')
    draw = ImageDraw.Draw(image)
    # Cover the entire unknown camera QR and its quiet zone with opaque pixels.
    draw.rounded_rectangle((476, 411, 715, 656), radius=18, fill='#FFFFFF')
    buffer = io.BytesIO()
    segno.make(WEBSITE, error='h', micro=False).save(buffer, kind='png', scale=5, border=4)
    buffer.seek(0)
    with Image.open(buffer) as qr_source:
        qr = qr_source.convert('RGB')
    x = 476 + (240 - qr.width) // 2
    y = 411 + (246 - qr.height) // 2
    image.paste(qr, (x, y))
    decoded = zxingcpp.read_barcode(image)
    if not decoded or decoded.text != WEBSITE:
        raise ValueError('The staged scanner QR does not decode to the OTP Harbor website.')
    return image


def write_review():
    if REVIEW.exists():
        resolved = REVIEW.resolve()
        if not resolved.is_relative_to((ROOT / 'staging').resolve()):
            raise ValueError('Review output escaped the ignored staging directory.')
        shutil.rmtree(REVIEW)
    records = []
    for platform in ('desktop', 'android'):
        for theme in ('dark', 'light'):
            for path in sorted((SOURCE / platform / theme).glob('*.png')):
                if path.stem == 'biometric-prompt':
                    continue  # This has its own reviewed transparent cutout workflow.
                relative = Path(platform) / theme / path.name
                before = REVIEW / 'before' / relative
                after = REVIEW / 'after' / relative
                before.parent.mkdir(parents=True, exist_ok=True)
                after.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(path, before)
                with Image.open(path) as original:
                    if platform == 'desktop':
                        cleaned, crop = clean_desktop(original)
                        operation = '3 px exterior crop + 20 px transparent corner mask'
                    else:
                        cleaned, crop = clean_android(original)
                        operation = 'remove Android status/navigation bars only'
                    cleaned.save(after, optimize=True)
                    assert cleaned.width <= original.width and cleaned.height <= original.height
                    if platform == 'desktop':
                        corner_atlas(original, REVIEW / 'corners/before' / relative)
                        corner_atlas(cleaned, REVIEW / 'corners/after' / relative)
                records.append({
                    'platform': platform, 'theme': theme, 'name': path.stem,
                    'before': ('before/' + relative.as_posix()),
                    'after': ('after/' + relative.as_posix()),
                    'sourceSha256': sha256(path), 'candidateSha256': sha256(after),
                    'crop': list(crop), 'operation': operation,
                    'status': 'candidate for user review',
                    'cornerBefore': ('corners/before/' + relative.as_posix()) if platform == 'desktop' else None,
                    'cornerAfter': ('corners/after/' + relative.as_posix()) if platform == 'desktop' else None,
                })

    scanner = safe_scanner_image()
    if scanner is not None:
        after = REVIEW / 'after/desktop/dark/scanner-live-review-only.png'
        after.parent.mkdir(parents=True, exist_ok=True)
        cleaned, crop = clean_desktop(scanner)
        cleaned.save(after, optimize=True)
        corner_atlas(cleaned, REVIEW / 'corners/after/desktop/dark/scanner-live-review-only.png')
        records.append({
            'platform': 'desktop', 'theme': 'dark', 'name': 'scanner-live-review-only',
            'before': None, 'after': 'after/desktop/dark/scanner-live-review-only.png',
            'sourceSha256': sha256(SCANNER_INPUT), 'candidateSha256': sha256(after),
            'crop': list(crop),
            'operation': 'unknown camera QR covered; verified website QR inserted; 3 px exterior crop + transparent corners',
            'status': 'designed safety composite; candidate for user review',
            'cornerBefore': None,
            'cornerAfter': 'corners/after/desktop/dark/scanner-live-review-only.png',
        })

    (REVIEW / 'report.json').write_text(json.dumps(records, indent=2) + '\n', encoding='utf-8')
    cards = []
    for item in records:
        title = html.escape(f"{item['platform'].title()} / {item['theme'].title()} / {item['name'].replace('-', ' ').title()}")
        before = (f'<a href="{item["before"]}"><img src="{item["before"]}" alt="Original {title}">Original</a>'
                  if item['before'] else '<div class="unavailable">Original QR source withheld: payload could not be verified.</div>')
        corners = (f'<div class="corners"><span><img src="{item["cornerBefore"]}" alt="Magnified original corners">Original corners</span>'
                   if item['cornerBefore'] else '<div class="corners"><span></span>')
        if item['cornerAfter']:
            corners += f'<span><img src="{item["cornerAfter"]}" alt="Magnified cleaned corners">Cleaned corners</span></div>'
        else:
            corners = ''
        cards.append(f'<article><h2>{title}</h2><div class="pair">{before}'
                     f'<a href="{item["after"]}"><img src="{item["after"]}" alt="Cleaned {title}">Cleaned candidate</a></div>'
                     f'{corners}<p>{html.escape(item["operation"])}</p></article>')
    document = '''<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>OTP Harbor screenshot cleanup review</title><style>
body{margin:0;padding:28px;background:#0c1c33;color:#f7f9ff;font:16px/1.5 system-ui,sans-serif}
main{max-width:1600px;margin:auto}h1{margin-bottom:5px}p{color:#c3d3e8}article{padding:24px;margin:24px 0;border:1px solid #36537c;border-radius:18px;background:#132744}
article h2{margin:0 0 16px}.pair{display:grid;grid-template-columns:1fr 1fr;gap:20px}.pair a{color:#7bdcff;text-decoration:none;font-weight:700}
.pair img{display:block;width:100%;height:440px;object-fit:contain;margin-bottom:8px;border-radius:10px;background:repeating-conic-gradient(#33506f 0 25%,#213a57 0 50%) 50%/24px 24px}
.unavailable{display:grid;place-items:center;min-height:440px;padding:20px;background:#0c1c33;border-radius:10px;color:#d0def0}
.corners{display:grid;grid-template-columns:1fr 1fr;gap:20px;margin-top:18px}.corners span{font-weight:700;color:#b8e8ff}.corners img{display:block;width:min(100%,280px);height:auto;margin-bottom:5px;border-radius:8px}
@media(max-width:760px){body{padding:14px}.pair{grid-template-columns:1fr}.pair img{height:350px}}
</style></head><body><main><h1>Screenshot cleanup review</h1><p>These are local candidates only. Canonical captures, Store slides, README and public website images are unchanged. Select an image to inspect it at full resolution.</p>
''' + '\n'.join(cards) + '</main></body></html>'
    (REVIEW / 'index.html').write_text(document, encoding='utf-8')
    if LOCAL_SITE.is_dir():
        published = LOCAL_SITE / 'screenshot-review'
        if published.exists():
            shutil.rmtree(published)
        shutil.copytree(REVIEW, published)
        print('Review gallery: http://127.0.0.1:8765/screenshot-review/')
    print(f'Staged {len(records)} review candidates in {REVIEW}')


if __name__ == '__main__':
    write_review()
