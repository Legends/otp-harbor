"""Make only the exterior corners of canonical desktop captures transparent."""

import argparse
import hashlib
import json
from pathlib import Path
import re

from PIL import Image

from screenshot_cleanup import mask_desktop_corners
from marketing_environment import require_pinned_dependencies


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--screen', default='settings')
    parser.add_argument('--radius', required=True, type=int, help='Native window radius in physical capture pixels.')
    parser.add_argument('--themes', nargs='+', choices=('light', 'dark'), default=('light', 'dark'))
    parser.add_argument('--replace', action='store_true', help='Update the canonical captures and record their original hashes.')
    args = parser.parse_args()
    require_pinned_dependencies()
    if not args.replace or not re.fullmatch(r'[a-z0-9]+(?:-[a-z0-9]+)*', args.screen):
        parser.error('Supply --replace and a lowercase, hyphen-separated screen name.')
    root = Path(__file__).resolve().parents[2] / 'artifacts/marketing'
    manifest_path = root / 'manifest.json'
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    records = {entry['path']: entry for entry in manifest['captures']}
    prepared = []
    for theme in dict.fromkeys(args.themes):
        path = root / 'source/desktop' / theme / f'{args.screen}.png'
        original_hash = hashlib.sha256(path.read_bytes()).hexdigest()
        with Image.open(path) as original:
            masked = mask_desktop_corners(original, args.radius)
            if masked.size != original.size or masked.convert('RGB').tobytes() != original.convert('RGB').tobytes():
                raise ValueError(f'Corner cleanup changed native capture pixels: {path}')
        prepared.append((path, original_hash, masked))
    for path, original_hash, masked in prepared:
        relative = path.relative_to(root).as_posix()
        prior = records.get(relative, {})
        temporary = path.with_suffix('.masking.png')
        masked.save(temporary, optimize=True)
        temporary.replace(path)
        records[relative] = {
            'path': relative, 'platform': 'desktop', 'theme': path.parent.name,
            'purpose': args.screen, 'method': 'manual live-app capture; lossless exterior corner alpha mask',
            'width': masked.width, 'height': masked.height,
            'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
            'originalSha256': prior.get('originalSha256', original_hash)
                if prior.get('sha256') == original_hash else original_hash,
            'cornerMaskRadius': args.radius,
        }
        print(f'Masked exterior corners: {relative} (radius {args.radius}px; UI RGB unchanged)')
    manifest['captures'] = sorted(records.values(), key=lambda entry: entry['path'])
    manifest['publicationStatus'] = 'review required: inspect every image, legal names and app state before publication'
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
