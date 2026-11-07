"""Safety boundaries for the binary repository; no private signing inputs required."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("fdroid_repository", ROOT / "scripts/release/Build-FDroidRepository.py")
repository = importlib.util.module_from_spec(spec)
spec.loader.exec_module(repository)


class RepositoryTests(unittest.TestCase):
    def setUp(self):
        self.settings = json.loads((ROOT / "packaging/fdroid/repository.json").read_text())

    def release(self, version="3.0.2", **overrides):
        tag = "android-v" + version
        names = [f"OTP-Harbor-android-universal-{version}.apk", "SHA256SUMS"]
        value = {"tag_name": tag, "draft": False, "prerelease": False, "assets": [
            {"name": name, "browser_download_url": f"https://github.com/Legends/otp-harbor/releases/download/{tag}/{name}"}
            for name in names]}
        value.update(overrides)
        return value

    def test_version_matches_production_mapping(self):
        self.assertEqual(("3.0.2", 30000299), repository.version_from_tag("android-v3.0.2"))
        self.assertLess(repository.version_from_tag("android-v3.0.99")[1], repository.version_from_tag("android-v3.1.0")[1])
        for tag in ("v3.0.2", "android-v3.0.2-rc1", "android-v210.0.0", "android-v3.100.0", "android-v3.0.1000", "android-v3.0.2/../x"):
            with self.subTest(tag=tag), self.assertRaises(ValueError):
                repository.version_from_tag(tag)

    def test_only_published_stable_production_versions_are_selected(self):
        values = [self.release("3.0.1"), self.release("3.0.4", draft=True), self.release("3.0.3", prerelease=True), self.release("3.0.2"), {"tag_name": "v5.0.0"}]
        self.assertEqual([30000299, 30000199], [r["code"] for r in repository.select_releases(values, self.settings)])
        with self.assertRaises(ValueError):
            repository.select_releases([self.release(draft=True)], self.settings)

    def test_duplicate_version_or_asset_is_rejected(self):
        with self.assertRaises(ValueError):
            repository.select_releases([self.release(), self.release()], self.settings)
        value = self.release()
        value["assets"].append(copy.deepcopy(value["assets"][0]))
        with self.assertRaises(ValueError):
            repository.select_releases([value], self.settings)

    def test_old_unretained_assets_do_not_block_current_updates(self):
        values = [self.release("3.0.0", assets=[]), self.release("3.0.1"), self.release("3.0.2")]
        self.assertEqual(2, len(repository.select_releases(values, self.settings)))

    def test_tool_failures_do_not_expose_raw_output(self):
        result = type('Result', (), {'returncode': 1, 'stdout': 'sensitive stdout', 'stderr': 'sensitive stderr'})()
        with patch.object(repository.subprocess, 'run', return_value=result):
            with self.assertRaises(RuntimeError) as caught:
                repository.command_output(['fdroid', 'update'])
        self.assertNotIn('sensitive', str(caught.exception))

    def test_missing_asset_or_untrusted_download_url_is_rejected(self):
        for mutation in (lambda r: r["assets"].pop(), lambda r: r["assets"][0].update(browser_download_url="https://example.com/untrusted.apk")):
            value = self.release()
            mutation(value)
            with self.assertRaises(ValueError):
                repository.select_releases([value], self.settings)

    def test_only_https_repository_and_separate_pinned_identities_are_allowed(self):
        repository.validate_settings(self.settings)
        for key, value in (("applicationId", "io.github.legends.otpharbor.debug"), ("url", "http://example.com/fdroid/repo"),
                           ("url", "https://user:password@example.com/fdroid/repo"), ("url", "https://example.com/fdroid/repo?token=private"),
                           ("indexCertificateSha256", "untrusted"), ("retainedVersions", 0),
                           ("indexCertificateSha256", self.settings["apkCertificateSha256"])):
            settings = {**self.settings, key: value}
            with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                repository.validate_settings(settings)

    def test_checksum_must_match_exact_filename_and_single_entry(self):
        with tempfile.TemporaryDirectory() as directory:
            apk = Path(directory) / "test.apk"
            apk.write_bytes(b"synthetic APK")
            digest = hashlib.sha256(apk.read_bytes()).hexdigest()
            self.assertEqual(digest, repository.verify_checksum(apk, f"{digest}  test.apk\n"))
            for text in (f"{digest}  other.apk", "0" * 64 + "  test.apk", f"{digest}  test.apk\n{digest}  test.apk"):
                with self.subTest(text=text), self.assertRaises(ValueError):
                    repository.verify_checksum(apk, text)

    def test_apk_must_match_certificate_package_version_and_release_configuration(self):
        release = repository.select_releases([self.release()], self.settings)[0]
        signature = "Signer #1 certificate SHA-256 digest: " + self.settings["apkCertificateSha256"].lower() + "\n"
        badging = "package: name='io.github.legends.otpharbor' versionCode='30000299' versionName='3.0.2'\n"
        repository.verify_apk_output(signature, badging, release, self.settings)
        failures = [("", badging), (signature.replace(self.settings["apkCertificateSha256"].lower(), "0" * 64), badging),
                    (signature + signature.replace("#1", "#2"), badging), (signature, badging.replace("otpharbor'", "otpharbor.debug'")),
                    (signature, badging.replace("30000299", "1")), (signature, badging.replace("3.0.2", "0.1.0-dev")),
                    (signature, badging + "application-debuggable\n")]
        for signature_value, badging_value in failures:
            with self.subTest(signature=signature_value, badging=badging_value), self.assertRaises(ValueError):
                repository.verify_apk_output(signature_value, badging_value, release, self.settings)

    def test_private_files_cannot_be_published(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = Path(directory)
            (repo / "index-v2.json").write_text("{}")
            repository.check_public_files(repo)
            for name in ("config.yml", "Config.yaml", "repository-index.p12", "private.jks", "password.key", "keystore-password.txt", "keystore-password.dpapi", ".private"):
                file = repo / name
                file.write_text("synthetic private material")
                with self.subTest(name=name), self.assertRaises(ValueError):
                    repository.check_public_files(repo)
                file.unlink()

    def test_setup_page_uses_the_same_https_address_and_certificate_pin(self):
        template = (ROOT / 'site/fdroid/index.html').read_text(encoding='utf-8')
        rendered = repository.render_page(template, self.settings)
        self.assertIn(self.settings['url'] + '/?fingerprint=' + self.settings['indexCertificateSha256'], rendered)
        self.assertNotIn('{{FDROID_', rendered)
        self.assertIn('separate from the official F-Droid catalog', rendered)
        with self.assertRaises(ValueError):
            repository.render_page(template.replace('{{FDROID_REPOSITORY_FINGERPRINT}}', ''), self.settings)


if __name__ == "__main__":
    unittest.main()
