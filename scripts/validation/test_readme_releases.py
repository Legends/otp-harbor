"""Deterministic coverage for the README release generator; no network required."""
import importlib.util
from pathlib import Path
import unittest
import re
from urllib.parse import parse_qs, urlsplit

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("readme_releases", ROOT / "scripts/release/Update-ReadmeReleases.py")
GENERATOR = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(GENERATOR)


def release(tag, **overrides):
    return dict(tag_name=tag, draft=False, prerelease=False,
                published_at="2026-11-06T13:01:34Z", assets=[]) | overrides


class ReadmeReleaseTests(unittest.TestCase):
    website = '\n'.join(f'<span data-release-channel="{channel}">2.0.1</span>'
                        for channel in ('store', 'desktop', 'android'))

    def test_website_stamps_store_package_and_stable_github_versions_independently(self):
        releases = [[release("v4.9.9"), release("android-v6.0.1")],
                    [release("v4.10.0"), release("v5.0.0", draft=True),
                     release("android-v7.0.0", prerelease=True)]]
        result = GENERATOR.update_website(self.website, releases, "3.2.65535.0")
        self.assertIn('data-release-channel="store">3.2.65535.0', result)
        self.assertIn('data-release-channel="desktop">4.10.0', result)
        self.assertIn('data-release-channel="android">6.0.1', result)
        self.assertEqual(result, GENERATOR.update_website(result, releases, "3.2.65535.0"))

    def test_website_preserves_legacy_android_apk_fallback(self):
        releases = [release("v3.0.2"), release("v2.0.1", assets=[
            {"name": "OTP-Harbor-android-universal-2.0.1.apk"}])]
        result = GENERATOR.update_website(self.website, releases, "3.2.65535.0")
        self.assertIn('data-release-channel="android">2.0.1', result)

    def test_website_rejects_invalid_store_version_or_duplicate_marker(self):
        releases = [release("v3.0.2"), release("android-v3.0.2")]
        for invalid in ("3.0.2", "3.2.65536.0", '<img src="invalid">'):
            with self.subTest(version=invalid), self.assertRaises(ValueError):
                GENERATOR.update_website(self.website, releases, invalid)
        with self.assertRaises(ValueError):
            GENERATOR.update_website(self.website + self.website, releases, "3.2.65535.0")

    def test_independent_channels_use_numeric_versions_and_ignore_drafts_and_prereleases(self):
        releases = [release("v3.9.9"), release("v3.10.0"), release("android-v2.0.2"),
                    {**release("v4.0.0"), "draft": True},
                    {**release("android-v4.0.0"), "prerelease": True}, release("v5.0.0-rc.1")]
        result = GENERATOR.render(releases, "Legends/otp-harbor")
        self.assertIn("[v3.10.0]", result)
        self.assertIn("[android-v2.0.2]", result)
        self.assertNotIn("v3.9.9", result)
        self.assertNotIn("v4.0.0", result)
        self.assertNotIn("v5.0.0", result)

    def test_pagination_release_dates_and_allowlisted_downloads(self):
        assets = [{"name": name, "browser_download_url": "https://untrusted.invalid"} for name in
                  ("OTP-Harbor-android-universal-3.0.2.apk", "SHA256SUMS", "readme|injection.md")]
        result = GENERATOR.render([[release("v3.0.1")], [release("android-v3.0.2", assets=assets)]], "Legends/otp-harbor")
        self.assertIn("2026-11-06", result)
        self.assertIn("/download/android-v3.0.2/OTP-Harbor-android-universal-3.0.2.apk", result)
        self.assertIn("[Checksums]", result)
        self.assertIn("[Release notes]", result)
        self.assertNotIn("untrusted", result)
        self.assertNotIn("injection", result)

    def test_update_is_idempotent_and_preserves_handwritten_content(self):
        original = f"handwritten intro\n{GENERATOR.START}\nstale\n{GENERATOR.END}\nhandwritten installation\n"
        result = GENERATOR.update_readme(original, [release("v3.0.2")], "Legends/otp-harbor")
        self.assertTrue(result.startswith("handwritten intro\n"))
        self.assertTrue(result.endswith("\nhandwritten installation\n"))
        self.assertEqual(result, GENERATOR.update_readme(result, [release("v3.0.2")], "Legends/otp-harbor"))

    def test_missing_duplicate_or_unordered_markers_fail_without_overwriting_content(self):
        for content in ("no markers", GENERATOR.START + GENERATOR.START + GENERATOR.END,
                        GENERATOR.END + GENERATOR.START):
            with self.subTest(content=content), self.assertRaises(ValueError):
                GENERATOR.update_readme(content, [], "Legends/otp-harbor")

    def test_no_stable_release_does_not_fabricate_downloads(self):
        result = GENERATOR.render([release("v3.0.0-rc.1")], "Legends/otp-harbor")
        self.assertEqual(2, result.count("No stable release published"))
        self.assertNotIn("/download/", result)

    def test_every_release_publication_refreshes_readme_even_with_github_token(self):
        workflow = (ROOT / ".github/workflows/build-and-test.yml").read_text(encoding="utf-8")
        self.assertIn("needs: [publish-avalonia-release, publish-android-release]", workflow)
        self.assertIn("uses: ./.github/workflows/readme-releases.yml", workflow)
        self.assertIn("GH_PAT: ${{ secrets.GH_PAT }}", workflow)
        updater = (ROOT / ".github/workflows/readme-releases.yml").read_text(encoding="utf-8")
        for trigger in ("workflow_call:", "workflow_dispatch:", "release:", "schedule:"):
            self.assertIn(trigger, updater)
        self.assertIn("--paginate --slurp", updater)
        self.assertIn("token: ${{ secrets.GH_PAT || github.token }}", updater)
        self.assertIn("Update-ReadmeReleases.py --website site/index.html", updater)
        self.assertIn('git add -- readme.md site/index.html packaging/windows-store/public-version.json', updater)
        self.assertIn("actions: write", updater)
        self.assertIn('gh workflow run pages.yml --repo "$GITHUB_REPOSITORY" --ref "$DEFAULT_BRANCH"', updater)
        pages = (ROOT / ".github/workflows/pages.yml").read_text(encoding="utf-8")
        self.assertIn("'packaging/windows-store/public-version.json'", pages)
        self.assertIn("Update-ReadmeReleases.py --website _site/index.html", pages)
        self.assertNotIn("gh pr create", updater)

    def test_version_badges_keep_three_distribution_channels_independent(self):
        readme = (ROOT / "readme.md").read_text(encoding="utf-8")
        badges = {alt: (image, target) for alt, image, target in re.findall(
            r"\[!\[([^\]]+)\]\(([^)]+)\)\]\(([^)]+)\)", readme)}
        store_image, store_link = badges["Microsoft Store version"]
        self.assertEqual("https://apps.microsoft.com/detail/9P31KH5L924P", store_link)
        self.assertIn("/badge/Microsoft%20Store-", store_image)
        self.assertIn("-0078D4?logo=microsoft", store_image)
        self.assertNotIn("/github/", store_image)
        for label, pattern, color in (("Desktop", "v*", "blue"), ("Android", "android-v*", "green")):
            image, link = badges[label + " Release"]
            parsed = urlsplit(image)
            self.assertEqual("/github/v/release/Legends/otp-harbor", parsed.path)
            query = parse_qs(parsed.query)
            self.assertEqual([pattern], query["filter"])
            self.assertEqual(["date"], query["sort"])
            self.assertEqual([label], query["label"])
            self.assertEqual([color], query["color"])
            self.assertNotIn("include_prereleases", query)
            self.assertEqual("https://github.com/Legends/otp-harbor/releases", link)


if __name__ == "__main__":
    unittest.main()
