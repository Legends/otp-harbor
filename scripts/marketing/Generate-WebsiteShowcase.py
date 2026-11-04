"""Build website campaign frames from the reviewed, cleaned app captures."""

import hashlib
import io
import json
from pathlib import Path

import segno
import zxingcpp
from PIL import Image, ImageDraw, ImageFont, ImageOps

from screenshot_cleanup import DESKTOP_CORNER_RADIUS, clean_android, clean_desktop


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
        'light': ('account-list', 'settings'),
    },
    'android': {
        'dark': ('account-list', 'settings'),
        'light': ('account-list', 'settings'),
    },
}
MARKETING = {
    'desktop': (
        ('import-formats', 'imports', 'Import more formats.',
         'Aegis JSON · 2FAS · otpauth:// lists\nGoogle Authenticator transfer QR images', None),
        ('qr-scanning', 'scanner', 'Scan a QR code.',
         'Account QR codes and Google Authenticator\ntransfer codes', None),
        ('backup-restore', 'backup-restore', 'Keep a way back.',
         'Encrypted backup and restore', None),
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
    'import-formats': 'Import Formats',
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


def scaled_capture(cleaned, platform):
    bounds = (860, 900) if platform == 'desktop' else (455, 900)
    scale = min(bounds[0] / cleaned.width, bounds[1] / cleaned.height, 1)
    size = (round(cleaned.width * scale), round(cleaned.height * scale))
    return cleaned.resize(size, Image.Resampling.LANCZOS)


def compose(platform, theme, screen, cleaned, background, icon, title=None, subtitle=None,
            chip=None):
    canvas = ImageOps.fit(background, (1920, 1080), method=Image.Resampling.LANCZOS).convert('RGBA')
    shade = Image.new('RGBA', canvas.size, (1, 10, 31, 0))
    fade = Image.new('L', (1920, 1))
    fade.putdata([204 if x < 610 else round(204 * max(0, (1180 - x) / 570))
                  for x in range(1920)])
    shade.putalpha(fade.resize(canvas.size))
    canvas.alpha_composite(shade)
    draw = ImageDraw.Draw(canvas)
    draw.rounded_rectangle((94, 68, 164, 138), radius=15, fill='#101e43')
    canvas.alpha_composite(icon, (98, 72))
    campaign_platform = 'WINDOWS' if platform == 'desktop' else 'ANDROID'
    draw.text((184, 86), f'OTP HARBOR  /  {campaign_platform}', font=font(23, True), fill='#EAF3FF')
    title_size = 66
    title = title or TITLES[screen]
    while draw.textbbox((0, 0), title, font=font(title_size, True))[2] > 755:
        title_size -= 2
    draw.text((98, 258), title, font=font(title_size, True), fill='#FFFFFF')
    if subtitle:
        draw.multiline_text((102, 381), subtitle,
                            font=font(28), fill='#C8E1F9', spacing=12)
    if chip:
        draw.rounded_rectangle((99, 872, 455, 932), radius=17,
                               fill='#132E57', outline='#36BDF4', width=2)
        draw.text((123, 888), chip, font=font(22, True), fill='#FFFFFF')

    app = scaled_capture(cleaned, platform)
    if platform == 'desktop':
        x = 970 + (870 - app.width) // 2
        y = 80 + (910 - app.height) // 2
        shadow = Image.new('RGBA', canvas.size)
        ImageDraw.Draw(shadow).rounded_rectangle(
            (x + 18, y + 23, x + app.width + 18, y + app.height + 23),
            radius=26, fill=(0, 4, 18, 120))
        canvas.alpha_composite(shadow)
        canvas.alpha_composite(app.convert('RGBA'), (x, y))
        frame = 'cleaned desktop window'
        radius = DESKTOP_CORNER_RADIUS
    else:
        frame_width, frame_height = app.width + 42, app.height + 42
        x = 1220 + (520 - frame_width) // 2
        y = (1080 - frame_height) // 2
        shadow = Image.new('RGBA', canvas.size)
        ImageDraw.Draw(shadow).rounded_rectangle(
            (x + 20, y + 25, x + frame_width + 20, y + frame_height + 25),
            radius=44, fill=(0, 4, 18, 140))
        canvas.alpha_composite(shadow)
        draw = ImageDraw.Draw(canvas)
        draw.rounded_rectangle((x, y, x + frame_width, y + frame_height),
                               radius=40, fill='#09172E', outline='#53D2FF', width=5)
        x += 21
        y += 21
        radius = 20
        mask = Image.new('L', app.size)
        ImageDraw.Draw(mask).rounded_rectangle((0, 0, app.width - 1, app.height - 1),
                                               radius=radius, fill=255)
        canvas.paste(app.convert('RGB'), (x, y), mask)
        frame = 'designed phone frame'
    return canvas.convert('RGB'), {'bounds': [x, y, app.width, app.height],
                                   'cornerMaskRadius': radius, 'frame': frame}


def add_illustration(canvas, path):
    with Image.open(path) as source:
        illustration = source.convert('RGB')
    illustration.thumbnail((555, 300), Image.Resampling.LANCZOS)
    x, y = 105, 510
    draw = ImageDraw.Draw(canvas)
    draw.text((x, y - 33), 'CONCEPT ILLUSTRATION', font=font(18, True), fill='#9DDFFF')
    draw.rounded_rectangle((x - 2, y - 2, x + illustration.width + 2,
                            y + illustration.height + 2), radius=18,
                           fill='#112A50', outline='#47C7F7', width=2)
    mask = Image.new('L', illustration.size)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, illustration.width - 1,
                                            illustration.height - 1), radius=15, fill=255)
    canvas.paste(illustration, (x, y), mask)
    return [x, y, illustration.width, illustration.height]


def github_demo_qr(size):
    buffer = io.BytesIO()
    segno.make(GITHUB_REPO, error='h', micro=False).save(buffer, kind='png', scale=10, border=4)
    buffer.seek(0)
    with Image.open(buffer) as qr_source:
        return qr_source.convert('RGB').resize((size, size), Image.Resampling.NEAREST)


def add_github_demo_qr(cleaned, platform, theme):
    if platform == 'desktop':
        if cleaned.size != (1394, 1044):
            raise ValueError('Desktop scanner dimensions changed; review the camera preview.')
        composite = cleaned.copy()
        draw = ImageDraw.Draw(composite)
        if theme == 'light':
            art_bounds = (372, 443, 1023, 811)
            draw.rectangle((372, 443, 1022, 810), fill='#EAF4FB')
            draw.rounded_rectangle((527, 453, 870, 806), radius=28,
                                   fill='#C8DFEF')
            draw.rounded_rectangle((522, 448, 873, 807), radius=28,
                                   fill='#F8FBFF', outline='#47AFD8', width=3)
            draw.rounded_rectangle((541, 460, 854, 795), radius=13,
                                   fill='#FFFFFF')
        else:
            art_bounds = (522, 448, 874, 808)
            draw.rounded_rectangle((528, 453, 870, 806), radius=28, fill='#061122')
            draw.rounded_rectangle((art_bounds[0], art_bounds[1],
                                    art_bounds[2] - 1, art_bounds[3] - 1),
                                   radius=28, fill='#101F38', outline='#64C7F5', width=3)
            draw.rounded_rectangle((541, 460, 854, 795), radius=13, fill='#FFFFFF')
        bounds = (563, 493, 833, 763)
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


def main():
    manifest = json.loads(MANIFEST.read_text(encoding='utf-8'))
    previous = manifest['generated']
    generated = [entry for entry in previous if not entry['path'].startswith('website/optimized/')
                 and entry['path'] != 'website/gallery.json']
    previous_paths = {entry['path'] for entry in previous if entry['path'].startswith('website/optimized/')}
    gallery = []
    written_direct = set()
    with Image.open(ICON) as original_icon:
        icon = original_icon.convert('RGBA').resize((62, 62), Image.Resampling.LANCZOS)
    for platform in ('desktop', 'android'):
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
                    visual, qr_bounds, art_bounds = add_github_demo_qr(cleaned, platform, theme)
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
                label = GALLERY_TITLES[slug]
                alt_kind = ('with a labeled GitHub demonstration QR in a genuine scanner capture'
                            if qr_composite else
                            'with a concept illustration and genuine app capture'
                            if illustration else 'with a genuine app capture')
                gallery.append({
                    'platform': platform, 'theme': theme, 'screen': slug,
                    'src': f'assets/marketing/framed/{platform}/{theme}/{slug}.png',
                    'width': 1920, 'height': 1080,
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
    print(f'Generated {len(gallery)} framed website captures and their cleaned sources.')


if __name__ == '__main__':
    main()
