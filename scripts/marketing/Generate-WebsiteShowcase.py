"""Build website campaign frames from the reviewed, cleaned app captures."""

import argparse
import hashlib
import io
import json
from pathlib import Path

import segno
import zxingcpp
from PIL import Image, ImageDraw, ImageFont, ImageOps
from scanner_marketing import desktop_demo_qr_bounds

from screenshot_cleanup import DESKTOP_CORNER_RADIUS, clean_android, clean_desktop
from marketing_environment import require_pinned_dependencies


REPO = Path(__file__).resolve().parents[2]
ROOT = REPO / 'artifacts/marketing'
SOURCE = ROOT / 'source'
OPTIMIZED = ROOT / 'website/optimized'
MANIFEST = ROOT / 'manifest.json'
GALLERY = ROOT / 'website/gallery.json'
GITHUB_REPO = 'https://github.com/Legends/otp-harbor'
ICON = REPO / 'OtpHarbor.UI.Avalonia.Desktop/Assets/Icons/app-1024.png'
BACKGROUNDS = {
    'desktop': REPO / 'packaging/windows-store/assets/source/store-marketing-screenshot-background.png',
    'android': REPO / 'packaging/android/marketing/source/android-marketing-background.png',
}
ORDER = {
    'desktop': {
        'dark': ('account-list', 'account-context-menu', 'settings'),
        'light': ('account-list', 'account-context-menu', 'settings'),
    },
    'android': {
        'dark': ('account-list', 'settings'),
        'light': ('account-list', 'settings'),
    },
}
MARKETING = {
    'desktop': (
        ('data-backup', 'data-backup', 'Data & backup.',
         'Import more formats. Keep a way back.\nAegis JSON · 2FAS · otpauth:// lists\nGoogle Authenticator transfer QR images\nEncrypted backup and restore', None),
        ('qr-scanning', 'scanner', 'Scan a QR code.',
         'Account QR codes and Google Authenticator\ntransfer codes', None),
    ),
    'android': (
        ('qr-scanning', 'scanner', 'Scan a QR code.',
         'Account and Google Authenticator\ntransfer QR codes · GitHub demo QR', None),
        ('backup-restore', 'settings', 'Keep a way back.',
         'Password-protected backup and restore',
         REPO / 'OtpHarbor.UI.Avalonia.Shared/Assets/Onboarding/move-accounts.png'),
    ),
}
TITLES = {
    'account-list': 'Account List',
    'settings': 'Settings',
    'data-backup': 'Data & Backup',
    'account-context-menu': 'Account Context Menu',
    'account-editor': 'Add with confidence.',
    'backup-top': 'Keep a way back.',
    'backup-restore': 'Back up and restore.',
    'imports': 'Bring your codes along.',
    'edit-swipe': 'Edit with a swipe.',
    'delete-swipe': 'Deliberate deletion.',
    'create-group': 'Keep accounts organized.',
}
GALLERY_TITLES = {
    'account-list': 'Account List',
    'account-context-menu': 'Account Context Menu',
    'settings': 'Settings',
    'data-backup': 'Data & Backup',
    'qr-scanning': 'QR Scanning',
    'backup-restore': 'Backup and Restore',
}


def digest(path):
    if path.suffix.lower() == '.json':
        text = path.read_text(encoding='utf-8').replace('\r\n', '\n').replace('\r', '\n')
        return hashlib.sha256(text.encode('utf-8')).hexdigest()
    return hashlib.sha256(path.read_bytes()).hexdigest()


def font(size, bold=False):
    windows = Path('C:/Windows/Fonts') / ('segoeuib.ttf' if bold else 'segoeui.ttf')
    return ImageFont.truetype(str(windows if windows.is_file() else
                                  ('DejaVuSans-Bold.ttf' if bold else 'DejaVuSans.ttf')), size)


def record(path, kind, parents, **metadata):
    return {
        'path': path.relative_to(ROOT).as_posix(),
        'sha256': digest(path),
        'kind': kind,
        'sourceSha256': [digest(parent) for parent in parents],
        **metadata,
    }


def scaled_capture(cleaned, platform, pixel_scale=1):
    bounds = tuple(round(value * pixel_scale) for value in
                   ((860, 900) if platform == 'desktop' else (455, 900)))
    scale = min(bounds[0] / cleaned.width, bounds[1] / cleaned.height, 1)
    size = (round(cleaned.width * scale), round(cleaned.height * scale))
    return cleaned.resize(size, Image.Resampling.LANCZOS)


def zoom_pixel_scale(cleaned, platform):
    base = scaled_capture(cleaned, platform)
    return min(2, cleaned.width / base.width, cleaned.height / base.height)


def compose(platform, theme, screen, cleaned, background, icon, title=None, subtitle=None,
            chip=None, pixel_scale=1):
    def px(value):
        return round(value * pixel_scale)

    def coords(*values):
        return tuple(px(value) for value in values)

    canvas = ImageOps.fit(background, coords(1920, 1080), method=Image.Resampling.LANCZOS).convert('RGBA')
    shade = Image.new('RGBA', canvas.size, (1, 10, 31, 0))
    fade = Image.new('L', (canvas.width, 1))
    fade.putdata([204 if x / pixel_scale < 610 else round(204 * max(0, (1180 - x / pixel_scale) / 570))
                  for x in range(canvas.width)])
    shade.putalpha(fade.resize(canvas.size))
    canvas.alpha_composite(shade)
    draw = ImageDraw.Draw(canvas)
    draw.rounded_rectangle(coords(94, 68, 164, 138), radius=px(15), fill='#101e43')
    canvas.alpha_composite(icon.resize(coords(62, 62), Image.Resampling.LANCZOS), coords(98, 72))
    campaign_platform = 'WINDOWS' if platform == 'desktop' else 'ANDROID'
    draw.text(coords(184, 86), f'OTP HARBOR  /  {campaign_platform}', font=font(px(23), True), fill='#EAF3FF')
    title_size = 66
    title = title or TITLES[screen]
    while draw.textbbox((0, 0), title, font=font(px(title_size), True))[2] > px(755):
        title_size -= 2
    draw.text(coords(98, 258), title, font=font(px(title_size), True), fill='#FFFFFF')
    if subtitle:
        draw.multiline_text(coords(102, 381), subtitle,
                            font=font(px(28)), fill='#C8E1F9', spacing=px(12))
    if chip:
        draw.rounded_rectangle(coords(99, 872, 455, 932), radius=px(17),
                               fill='#132E57', outline='#36BDF4', width=px(2))
        draw.text(coords(123, 888), chip, font=font(px(22), True), fill='#FFFFFF')

    app = scaled_capture(cleaned, platform, pixel_scale)
    if platform == 'desktop':
        x = px(970) + (px(870) - app.width) // 2
        y = px(80) + (px(910) - app.height) // 2
        shadow = Image.new('RGBA', canvas.size)
        ImageDraw.Draw(shadow).rounded_rectangle(
            (x + px(18), y + px(23), x + app.width + px(18), y + app.height + px(23)),
            radius=px(26), fill=(0, 4, 18, 120))
        canvas.alpha_composite(shadow)
        canvas.alpha_composite(app.convert('RGBA'), (x, y))
        frame = 'cleaned desktop window'
        radius = DESKTOP_CORNER_RADIUS
    else:
        frame_width, frame_height = app.width + px(42), app.height + px(42)
        x = px(1220) + (px(520) - frame_width) // 2
        y = (px(1080) - frame_height) // 2
        shadow = Image.new('RGBA', canvas.size)
        ImageDraw.Draw(shadow).rounded_rectangle(
            (x + px(20), y + px(25), x + frame_width + px(20), y + frame_height + px(25)),
            radius=px(44), fill=(0, 4, 18, 140))
        canvas.alpha_composite(shadow)
        draw = ImageDraw.Draw(canvas)
        draw.rounded_rectangle((x, y, x + frame_width, y + frame_height),
                               radius=px(40), fill='#09172E', outline='#53D2FF', width=px(5))
        x += px(21)
        y += px(21)
        radius = px(20)
        mask = Image.new('L', app.size)
        ImageDraw.Draw(mask).rounded_rectangle((0, 0, app.width - 1, app.height - 1),
                                               radius=radius, fill=255)
        canvas.paste(app.convert('RGB'), (x, y), mask)
        frame = 'designed phone frame'
    return canvas.convert('RGB'), {'bounds': [x, y, app.width, app.height],
                                   'cornerMaskRadius': radius, 'frame': frame}


def add_illustration(canvas, path, pixel_scale=1):
    def px(value):
        return round(value * pixel_scale)
    with Image.open(path) as source:
        illustration = source.convert('RGB')
    illustration.thumbnail((px(555), px(300)), Image.Resampling.LANCZOS)
    x, y = px(105), px(510)
    draw = ImageDraw.Draw(canvas)
    draw.text((x, y - px(33)), 'CONCEPT ILLUSTRATION', font=font(px(18), True), fill='#9DDFFF')
    draw.rounded_rectangle((x - px(2), y - px(2), x + illustration.width + px(2),
                            y + illustration.height + px(2)), radius=px(18),
                           fill='#112A50', outline='#47C7F7', width=px(2))
    mask = Image.new('L', illustration.size)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, illustration.width - 1,
                                            illustration.height - 1), radius=px(15), fill=255)
    canvas.paste(illustration, (x, y), mask)
    return [x, y, illustration.width, illustration.height]


def github_demo_qr(size):
    buffer = io.BytesIO()
    segno.make(GITHUB_REPO, error='h', micro=False).save(buffer, kind='png', scale=10, border=4)
    buffer.seek(0)
    with Image.open(buffer) as qr_source:
        return qr_source.convert('RGB').resize((size, size), Image.Resampling.NEAREST)


def add_github_demo_qr(cleaned, platform):
    if platform == 'desktop':
        composite = cleaned.copy()
        bounds = art_bounds = desktop_demo_qr_bounds(cleaned.size)
    else:
        if cleaned.size != (1220, 2532):
            raise ValueError('Android scanner dimensions changed; review the QR placement.')
        composite = cleaned.convert('RGB').copy()
        bounds = (305, 950, 915, 1560)
        art_bounds = bounds
    qr = github_demo_qr(bounds[2] - bounds[0])
    composite.paste(qr, bounds[:2])
    decoded = zxingcpp.read_barcode(composite)
    if not decoded or decoded.text != GITHUB_REPO:
        raise ValueError('The demo QR does not decode to the OTP Harbor repository.')
    return composite, bounds, art_bounds


def selected_website_asset(entry, platforms):
    return entry['path'].startswith('website/optimized/') and any(
        f'/{platform}/' in entry['path'] for platform in platforms)


def add_previews(manifest, gallery, platforms=('desktop', 'android')):
    """Derive delivery assets without changing reviewed captures or full-size frames."""
    generated = [entry for entry in manifest['generated']
                 if entry['kind'] != 'website-gallery-data'
                 and not (entry['kind'] == 'website-preview' and selected_website_asset(entry, platforms))]
    for item in gallery['items']:
        if item['platform'] not in platforms:
            continue
        full = item['full'] if 'full' in item else {key: item[key] for key in ('src', 'width', 'height')}
        frame = item.get('frame', full)
        source = OPTIMIZED / frame['src'].removeprefix('assets/marketing/')
        relative = Path('previews') / item['platform'] / item['theme'] / (item['screen'] + '.webp')
        target = OPTIMIZED / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        with Image.open(source) as original:
            preview = original.convert('RGB').resize((1200, 675), Image.Resampling.LANCZOS)
        preview.save(target, format='WEBP', quality=95, method=6)
        generated.append(record(target, 'website-preview', [source],
                                fullPath=source.relative_to(ROOT).as_posix(),
                                width=1200, height=675, quality=95))
        for key in ('src', 'width', 'height'):
            item.pop(key, None)
        item['preview'] = {'src': 'assets/marketing/' + relative.as_posix(),
                           'width': 1200, 'height': 675}
        item['full'] = full
    gallery['schemaVersion'] = 2
    GALLERY.write_text(json.dumps(gallery, indent=2) + '\n', encoding='utf-8')
    generated.append(record(GALLERY, 'website-gallery-data',
                            [OPTIMIZED / item['full']['src'].removeprefix('assets/marketing/')
                             for item in gallery['items']]))
    manifest['generated'] = sorted(generated, key=lambda entry: entry['path'])
    manifest['publicationStatus'] = 'review required: inspect every image, legal names and app state before publication'
    MANIFEST.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')


def add_lossless_fulls(manifest, gallery, platforms=('desktop', 'android')):
    """Deliver the same native pixels with smaller lossless WebP payloads."""
    generated = [entry for entry in manifest['generated']
                 if entry['kind'] != 'website-gallery-data'
                 and not (entry['kind'] == 'website-full-lossless' and selected_website_asset(entry, platforms))]
    metadata = {entry['path']: entry for entry in generated}
    for item in gallery['items']:
        if item['platform'] not in platforms:
            continue
        png = (OPTIMIZED / item['full']['src'].removeprefix('assets/marketing/')).with_suffix('.png')
        target = png.with_suffix('.webp')
        with Image.open(png) as source:
            source.save(target, format='WEBP', lossless=True, method=6, exact=True)
            with Image.open(target) as delivered:
                if source.convert('RGBA').tobytes() != delivered.convert('RGBA').tobytes():
                    raise ValueError(f'Lossless delivery changed source pixels: {target}')
            if target.stat().st_size >= png.stat().st_size:
                raise ValueError(f'Lossless delivery is not smaller: {target}')
        generated.append(record(target, 'website-full-lossless', [png],
                                pngPath=png.relative_to(ROOT).as_posix(),
                                sourcePath=metadata[png.relative_to(ROOT).as_posix()]['sourcePath'],
                                platform=item['platform'], theme=item['theme'], lossless=True,
                                width=item['full']['width'], height=item['full']['height']))
        item['full']['src'] = 'assets/marketing/' + target.relative_to(OPTIMIZED).as_posix()
    GALLERY.write_text(json.dumps(gallery, indent=2) + '\n', encoding='utf-8')
    generated.append(record(GALLERY, 'website-gallery-data',
                            [OPTIMIZED / item['full']['src'].removeprefix('assets/marketing/')
                             for item in gallery['items']]))
    manifest['generated'] = sorted(generated, key=lambda entry: entry['path'])
    MANIFEST.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--previews-only', action='store_true',
                        help='Keep existing reviewed full-size frames and canonical captures byte-for-byte.')
    parser.add_argument('--lossless-fulls-only', action='store_true',
                        help='Keep reviewed PNGs and previews; derive pixel-identical lossless delivery WebPs.')
    parser.add_argument('--platform', choices=('all', 'desktop', 'android'), default='all')
    args = parser.parse_args()
    require_pinned_dependencies()
    platforms = ('desktop', 'android') if args.platform == 'all' else (args.platform,)
    manifest = json.loads(MANIFEST.read_text(encoding='utf-8'))
    if args.lossless_fulls_only:
        add_lossless_fulls(manifest, json.loads(GALLERY.read_text(encoding='utf-8')), platforms)
        print('Generated pixel-identical lossless WebP full-size delivery images.')
        return
    if args.previews_only:
        add_previews(manifest, json.loads(GALLERY.read_text(encoding='utf-8')), platforms)
        print('Generated quality-95 WebP previews; publication review required.')
        return
    previous = manifest['generated']
    generated = [entry for entry in previous if not selected_website_asset(entry, platforms)
                 and entry['path'] != 'website/gallery.json']
    previous_paths = {entry['path'] for entry in previous if selected_website_asset(entry, platforms)}
    previous_gallery = json.loads(GALLERY.read_text(encoding='utf-8'))['items'] if GALLERY.exists() else []
    gallery = []
    written_direct = set()
    with Image.open(ICON) as original_icon:
        icon = original_icon.convert('RGBA')
    for platform in ('desktop', 'android'):
        if platform not in platforms:
            gallery.extend(item for item in previous_gallery if item['platform'] == platform)
            continue
        background_path = BACKGROUNDS[platform]
        with Image.open(background_path) as original_background:
            background = original_background.convert('RGB')
        for theme in ('dark', 'light'):
            items = [(screen, screen, None, None, None) for screen in ORDER[platform][theme]]
            items += list(MARKETING[platform])
            for slug, screen, title, subtitle, illustration in items:
                source = SOURCE / platform / theme / f'{screen}.png'
                if not source.is_file():
                    raise FileNotFoundError(f'Missing reviewed capture: {source}')
                with Image.open(source) as original:
                    cleaned, crop = (clean_desktop(original) if platform == 'desktop'
                                     else clean_android(original))
                direct = OPTIMIZED / platform / theme / source.name
                direct.parent.mkdir(parents=True, exist_ok=True)
                if direct not in written_direct:
                    cleaned.save(direct, optimize=True)
                    written_direct.add(direct)
                    generated.append(record(direct, 'cleaned-website-capture', [source],
                                            platform=platform, theme=theme,
                                            sourcePath=source.relative_to(ROOT).as_posix(),
                                            sourceCrop=list(crop),
                                            **({'cornerMaskRadius': DESKTOP_CORNER_RADIUS}
                                               if platform == 'desktop' else {})))
                visual = cleaned
                qr_composite = None
                if slug == 'qr-scanning':
                    visual, qr_bounds, art_bounds = add_github_demo_qr(cleaned, platform)
                    qr_composite = OPTIMIZED / 'composites' / platform / theme / 'scanner-project-repo-qr.png'
                    qr_composite.parent.mkdir(parents=True, exist_ok=True)
                    visual.save(qr_composite, optimize=True)
                    generated.append(record(qr_composite, 'designed-scanner-repo-qr', [source],
                                            platform=platform, theme=theme,
                                            sourcePath=source.relative_to(ROOT).as_posix(),
                                            sourceCrop=list(crop), qrBounds=list(qr_bounds),
                                            artBounds=list(art_bounds),
                                            qrPayload=GITHUB_REPO))
                designed, placement = compose(
                    platform, theme, screen, visual, background, icon, title, subtitle,
                    'GITHUB DEMO QR' if qr_composite else None)
                art_bounds = add_illustration(designed, illustration) if illustration else None
                if qr_composite:
                    decoded = zxingcpp.read_barcode(designed)
                    if not decoded or decoded.text != GITHUB_REPO:
                        raise ValueError('The framed demo QR cannot be scanned.')
                framed = OPTIMIZED / 'framed' / platform / theme / f'{slug}.png'
                framed.parent.mkdir(parents=True, exist_ok=True)
                designed.save(framed, optimize=True)
                parents = [source, background_path, ICON] + ([illustration] if illustration else [])
                generated.append(record(framed, 'designed-website-campaign',
                                        parents, platform=platform,
                                        theme=theme, sourcePath=source.relative_to(ROOT).as_posix(),
                                        cleanedPath=(qr_composite or direct).relative_to(ROOT).as_posix(),
                                        backgroundPath=background_path.relative_to(REPO).as_posix(),
                                        sourceCrop=list(crop), appPlacement=placement,
                                        **({'qrCompositePath': qr_composite.relative_to(ROOT).as_posix(),
                                            'qrPayload': GITHUB_REPO} if qr_composite else {}),
                                        **({'visualKind': 'marketing-promotion'} if title else {}),
                                        **({'illustrationPath': illustration.relative_to(REPO).as_posix(),
                                            'illustrationBounds': art_bounds} if illustration else {})))
                pixel_scale = zoom_pixel_scale(visual, platform)
                zoom_image, zoom_placement = compose(
                    platform, theme, screen, visual, background, icon, title, subtitle,
                    'GITHUB DEMO QR' if qr_composite else None, pixel_scale=pixel_scale)
                zoom_art = add_illustration(zoom_image, illustration, pixel_scale) if illustration else None
                zoom = OPTIMIZED / 'zoom' / platform / theme / f'{slug}.png'
                zoom.parent.mkdir(parents=True, exist_ok=True)
                zoom_image.save(zoom, optimize=True)
                zoom_metadata = {key: value for key, value in generated[-1].items()
                                 if key not in ('path', 'sha256', 'kind', 'sourceSha256')}
                zoom_metadata.update(pixelScale=pixel_scale, appPlacement=zoom_placement)
                if zoom_art:
                    zoom_metadata['illustrationBounds'] = zoom_art
                generated.append(record(zoom, 'designed-website-zoom', parents, **zoom_metadata))
                label = GALLERY_TITLES[slug]
                alt_kind = ('with an isolated render of the production Data & backup view using synthetic state'
                            if platform == 'desktop' and screen == 'data-backup' else
                            'with a labeled GitHub demonstration QR in a genuine scanner capture'
                            if qr_composite else
                            'with a concept illustration and genuine app capture'
                            if illustration else 'with a genuine app capture')
                gallery.append({
                    'platform': platform, 'theme': theme, 'screen': slug,
                    'frame': {'src': f'assets/marketing/framed/{platform}/{theme}/{slug}.png',
                              'width': 1920, 'height': 1080},
                    'full': {'src': f'assets/marketing/zoom/{platform}/{theme}/{slug}.png',
                             'width': zoom_image.width, 'height': zoom_image.height},
                    'alt': f'OTP Harbor {platform} {label.lower()} in {theme} theme, {alt_kind}',
                    'caption': label,
                })
    GALLERY.write_text(json.dumps({'items': gallery}, indent=2) + '\n', encoding='utf-8')
    generated.append(record(GALLERY, 'website-gallery-data',
                            [ROOT / entry['sourcePath'] for entry in generated
                             if entry['kind'] == 'designed-website-campaign']))
    manifest['generated'] = sorted(generated, key=lambda entry: entry['path'])
    manifest['publicationStatus'] = 'review required: inspect every image, legal names and app state before publication'
    MANIFEST.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    current_paths = {entry['path'] for entry in generated}
    for relative in previous_paths - current_paths:
        stale = ROOT / relative
        if stale.is_file():
            stale.unlink()
    add_previews(manifest, {'items': gallery}, platforms)
    add_lossless_fulls(manifest, json.loads(GALLERY.read_text(encoding='utf-8')), platforms)
    print(f'Generated {len(gallery)} framed website captures, cleaned sources and WebP previews.')


if __name__ == '__main__':
    main()
