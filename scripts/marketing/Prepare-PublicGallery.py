"""Select approved gallery heroes in HTML before publishing, never after navigation."""
import argparse
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def prepare(output, manifest):
    if manifest.get('publicationStatus') != 'approved for website publication':
        return False
    gallery = json.loads((output / 'assets/marketing/gallery.json').read_text(encoding='utf-8'))
    if gallery.get('schemaVersion') != 2:
        raise ValueError('The approved gallery must use preview/full schema version 2.')
    for platform, page, identifier, prefix in (
        ('desktop', 'index.html', 'hero-product-image', ''),
        ('android', 'android/index.html', 'android-hero-image', '../'),
    ):
        item = next(item for item in gallery['items'] if item['platform'] == platform
                    and item['theme'] == 'dark' and item['screen'] == 'account-list')
        preview = item['preview']
        if not (output / preview['src']).is_file():
            raise FileNotFoundError(preview['src'])
        path = output / page
        html = path.read_text(encoding='utf-8')
        def hero(match):
            tag = match[0]
            for name, value in (('src', prefix + preview['src']), ('width', preview['width']),
                                ('height', preview['height'])):
                tag = re.sub(rf'\b{name}="[^"]*"', f'{name}="{value}"', tag)
            return tag
        html, count = re.subn(rf'<img\b[^>]*\bid="{identifier}"[^>]*>', hero, html)
        if count != 1:
            raise ValueError(f'Expected one {identifier}')
        html = html.replace('class="product-shot"', 'class="product-shot is-framed"')
        html = re.sub(r'class="hero(?=[" ])', 'class="hero has-framed-hero', html, count=1)
        if platform == 'android':
            # Remove legacy lazy PNGs before the browser can discover/request them.
            html, count = re.subn(r'    <section id="android-campaign-legacy".*?</section>\s*', '', html, flags=re.S)
            if count != 1:
                raise ValueError('Expected one legacy Android campaign section')
        path.write_text(html, encoding='utf-8', newline='\n')
    return True


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / '_site')
    parser.add_argument('--local-review', action='store_true', help='Preview candidates in ignored local marketing staging only.')
    args = parser.parse_args()
    manifest = json.loads((ROOT / 'artifacts/marketing/manifest.json').read_text(encoding='utf-8'))
    if args.local_review:
        if not args.output.resolve().is_relative_to((ROOT / 'artifacts/marketing/staging').resolve()):
            parser.error('--local-review is restricted to local marketing staging')
        manifest['publicationStatus'] = 'approved for website publication'
    approved = prepare(args.output, manifest)
    print('Selected approved preview heroes.' if approved else 'Publication gate retained: using existing fallback heroes.')
