"""Regression coverage for lossless, reusable marketing screenshot cleanup."""

import importlib.util
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

from PIL import Image, ImageChops

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts/marketing'))
from screenshot_cleanup import clean_desktop, mask_desktop_corners
from marketing_environment import require_pinned_dependencies


class ScreenshotCleanupTests(unittest.TestCase):
    def test_mismatched_processing_versions_are_rejected_before_generation(self):
        with patch('marketing_environment.version', return_value='0.0.0'):
            with self.assertRaisesRegex(RuntimeError, 'Marketing dependencies differ from CI'):
                require_pinned_dependencies()

    def test_rounding_preserves_every_rgb_pixel_and_native_window_edge(self):
        for background in ('#ffffff', '#0c1c33'):
            with self.subTest(background=background):
                source = Image.new('RGBA', (2250, 1630), background)
                source.putpixel((0, 0), (97, 214, 214, 255))
                masked = mask_desktop_corners(source, 45)
                self.assertEqual(source.convert('RGB').tobytes(), masked.convert('RGB').tobytes())
                self.assertEqual(source.size, masked.size)
                for point in ((0, 0), (2249, 0), (0, 1629), (2249, 1629), (10, 10)):
                    self.assertEqual(0, masked.getpixel(point)[3])
                for point in ((45, 45), (1125, 0), (0, 815), (1125, 1629)):
                    self.assertEqual(255, masked.getpixel(point)[3])
                self.assertIsNone(ImageChops.difference(masked, mask_desktop_corners(masked, 45)).getbbox())

    def test_existing_transparency_survives_source_mask_and_website_cleanup(self):
        source = Image.new('RGBA', (200, 140), (12, 28, 51, 255))
        source.putpixel((100, 70), (5, 6, 7, 70))
        source.putpixel((12, 12), (5, 6, 7, 0))
        masked = mask_desktop_corners(source, 45)
        cleaned, crop = clean_desktop(masked)
        self.assertEqual((3, 3, 197, 137), crop)
        self.assertEqual(70, cleaned.getpixel((97, 67))[3])
        self.assertEqual(0, cleaned.getpixel((9, 9))[3])
        self.assertEqual(source.crop(crop).convert('RGB').tobytes(), cleaned.convert('RGB').tobytes())

    def test_invalid_radius_is_rejected(self):
        source = Image.new('RGB', (80, 60))
        for radius in (-1, 0, 31):
            with self.subTest(radius=radius), self.assertRaises(ValueError):
                mask_desktop_corners(source, radius)

    def test_desktop_feature_cards_share_settings_without_missing_tab_captures(self):
        spec = importlib.util.spec_from_file_location('website_showcase', ROOT / 'scripts/marketing/Generate-WebsiteShowcase.py')
        showcase = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(showcase)
        self.assertIn('settings', showcase.ORDER['desktop']['light'])
        self.assertIn('settings', showcase.ORDER['desktop']['dark'])
        screens = {slug: screen for slug, screen, *_ in showcase.MARKETING['desktop']}
        self.assertEqual('settings', screens['import-formats'])
        self.assertEqual('settings', screens['backup-restore'])
        self.assertEqual('scanner', screens['qr-scanning'])
        for folder in ('desktop', 'framed/desktop', 'previews/desktop', 'composites/desktop'):
            self.assertTrue(showcase.selected_website_asset({'path': f'website/optimized/{folder}/dark/settings.png'}, ('desktop',)))
        for folder in ('android', 'framed/android', 'previews/android', 'composites/android'):
            self.assertFalse(showcase.selected_website_asset({'path': f'website/optimized/{folder}/dark/settings.png'}, ('desktop',)))

    def test_desktop_refresh_retains_android_previews_and_gallery_metadata(self):
        spec = importlib.util.spec_from_file_location('website_showcase', ROOT / 'scripts/marketing/Generate-WebsiteShowcase.py')
        showcase = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(showcase)
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            optimized = root / 'website/optimized'
            generated, items = [], []
            for platform in ('desktop', 'android'):
                full = optimized / f'framed/{platform}/dark/settings.png'
                full.parent.mkdir(parents=True)
                Image.new('RGB', (1920, 1080), '#0c1c33').save(full)
                items.append({'platform': platform, 'theme': 'dark', 'screen': 'settings',
                              'full': {'src': f'assets/marketing/framed/{platform}/dark/settings.png',
                                       'width': 1920, 'height': 1080}})
            android_preview = optimized / 'previews/android/dark/settings.webp'
            android_preview.parent.mkdir(parents=True)
            Image.new('RGB', (1200, 675), '#ffffff').save(android_preview)
            android_entry = {'path': android_preview.relative_to(root).as_posix(), 'kind': 'website-preview', 'sha256': 'previous-review'}
            generated.append(android_entry.copy())
            items[1]['preview'] = {'src': 'assets/marketing/previews/android/dark/settings.webp', 'width': 1200, 'height': 675}
            previous_item = copy.deepcopy(items[1])
            previous_bytes = android_preview.read_bytes()
            manifest = {'generated': generated}
            gallery = {'items': items}
            with patch.multiple(showcase, ROOT=root, OPTIMIZED=optimized, GALLERY=root/'gallery.json', MANIFEST=root/'manifest.json'):
                showcase.add_previews(manifest, gallery, ('desktop',))
            self.assertEqual(previous_bytes, android_preview.read_bytes())
            self.assertEqual(previous_item, gallery['items'][1])
            self.assertIn(android_entry, manifest['generated'])
            self.assertEqual(2, json.loads((root/'gallery.json').read_text())['schemaVersion'])
            self.assertTrue((optimized/'previews/desktop/dark/settings.webp').is_file())


if __name__ == '__main__':
    unittest.main()
