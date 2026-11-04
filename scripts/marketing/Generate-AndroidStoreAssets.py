"""Generate Android submission assets from reviewed v3 marketing derivatives."""

import hashlib
import json
import shutil
from pathlib import Path

from PIL import Image, ImageOps


REPO = Path(__file__).resolve().parents[2]
ROOT = REPO / 'artifacts/marketing'
MANIFEST = ROOT / 'manifest.json'
STORE_ROOT = ROOT / 'store/android'
APP_ICON = REPO / 'OtpHarbor.UI.Avalonia.Desktop/Assets/Icons/app-1024.png'

WEBSITE_CAMPAIGN = (
    ('01-dark-account-list.png', 'dark', 'account-list'),
    ('02-dark-settings.png', 'dark', 'settings'),
    ('03-dark-qr-scanning.png', 'dark', 'qr-scanning'),
    ('04-dark-backup-restore.png', 'dark', 'backup-restore'),
    ('05-light-account-list.png', 'light', 'account-list'),
    ('06-light-settings.png', 'light', 'settings'),
    ('07-light-qr-scanning.png', 'light', 'qr-scanning'),
    ('08-light-backup-restore.png', 'light', 'backup-restore'),
)
PHONE_SCREENSHOTS = (
    ('01-dark-account-list-1080x1920.png', 'dark', 'account-list'),
    ('02-dark-settings-1080x1920.png', 'dark', 'settings'),
    ('03-dark-scanner-1080x1920.png', 'dark', 'scanner'),
    ('04-light-account-list-1080x1920.png', 'light', 'account-list'),
    ('05-light-settings-1080x1920.png', 'light', 'settings'),
    ('06-light-scanner-1080x1920.png', 'light', 'scanner'),
)


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def record(path: Path, kind: str, parents: list[Path], **metadata) -> dict:
    return {
        'path': path.relative_to(ROOT).as_posix(),
        'sha256': digest(path),
        'kind': kind,
        'platform': 'android',
        'sourceSha256': [digest(parent) for parent in parents],
        **metadata,
    }


def main() -> None:
    manifest = json.loads(MANIFEST.read_text(encoding='utf-8'))
    previous = manifest.get('generated', [])
    generated = [entry for entry in previous
                 if not entry['path'].startswith('store/android/')]
    previous_paths = {entry['path'] for entry in previous
                      if entry['path'].startswith('store/android/')}

    website_root = STORE_ROOT / 'website-campaign/en-US'
    for filename, theme, screen in WEBSITE_CAMPAIGN:
        source = ROOT / f'website/optimized/framed/android/{theme}/{screen}.png'
        if not source.is_file():
            raise FileNotFoundError(f'Missing reviewed Android campaign: {source}')
        destination = website_root / filename
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, destination)
        generated.append(record(
            destination, 'android-store-website-campaign', [source],
            theme=theme, screen=screen,
            sourcePath=source.relative_to(ROOT).as_posix(),
            publicationUse='Website campaign and store-listing candidate; manual review required'))

    phone_root = STORE_ROOT / 'phone-screenshots'
    for filename, theme, screen in PHONE_SCREENSHOTS:
        source = ROOT / f'website/optimized/android/{theme}/{screen}.png'
        if not source.is_file():
            raise FileNotFoundError(f'Missing reviewed Android capture: {source}')
        with Image.open(source) as original:
            app = original.convert('RGB')
            scale = min(1080 / app.width, 1920 / app.height, 1)
            size = (round(app.width * scale), round(app.height * scale))
            app = app.resize(size, Image.Resampling.LANCZOS)
        background = '#071121' if theme == 'dark' else '#E8EEF7'
        canvas = Image.new('RGB', (1080, 1920), background)
        position = ((1080 - app.width) // 2, (1920 - app.height) // 2)
        canvas.paste(app, position)
        destination = phone_root / filename
        destination.parent.mkdir(parents=True, exist_ok=True)
        canvas.save(destination, optimize=True)
        generated.append(record(
            destination, 'android-store-phone-screenshot', [source],
            theme=theme, screen=screen,
            sourcePath=source.relative_to(ROOT).as_posix(),
            appBounds=[position[0], position[1], app.width, app.height],
            background=background,
            publicationUse='Android store/Fastlane phone screenshot candidate; manual review required'))

    feature_source = ROOT / 'website/optimized/framed/android/dark/account-list.png'
    with Image.open(feature_source) as original:
        feature = ImageOps.fit(original.convert('RGB'), (1024, 500),
                               method=Image.Resampling.LANCZOS)
    feature_path = STORE_ROOT / 'featureGraphic.png'
    feature_path.parent.mkdir(parents=True, exist_ok=True)
    feature.save(feature_path, optimize=True)
    generated.append(record(
        feature_path, 'android-store-feature-graphic', [feature_source],
        sourcePath=feature_source.relative_to(ROOT).as_posix(),
        fit='center-crop',
        publicationUse='Android store/Fastlane feature graphic candidate; manual review required'))

    with Image.open(APP_ICON) as original:
        icon = original.convert('RGBA').resize((512, 512), Image.Resampling.LANCZOS)
    icon_path = STORE_ROOT / 'icon.png'
    icon.save(icon_path, optimize=True)
    generated.append(record(
        icon_path, 'android-store-high-resolution-icon', [APP_ICON],
        sourcePath=APP_ICON.relative_to(REPO).as_posix(),
        publicationUse='Android store/Fastlane high-resolution icon candidate; manual review required'))

    manifest['generated'] = sorted(generated, key=lambda entry: entry['path'])
    manifest['androidWebsiteCampaignOrder'] = [
        {'path': f'store/android/website-campaign/en-US/{filename}',
         'theme': theme, 'screen': screen}
        for filename, theme, screen in WEBSITE_CAMPAIGN
    ]
    manifest['androidPhoneScreenshotOrder'] = [
        {'path': f'store/android/phone-screenshots/{filename}',
         'theme': theme, 'screen': screen}
        for filename, theme, screen in PHONE_SCREENSHOTS
    ]
    MANIFEST.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')

    current_paths = {entry['path'] for entry in generated}
    for relative in previous_paths - current_paths:
        stale = ROOT / relative
        if stale.is_file():
            stale.unlink()
    print('Generated 8 Android website campaign images, 6 phone screenshots, feature graphic, and icon.')


if __name__ == '__main__':
    main()
