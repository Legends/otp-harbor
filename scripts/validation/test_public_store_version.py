"""Public Store version selection and fail-safe metadata synchronization tests."""
from datetime import datetime, timezone
from contextlib import redirect_stdout
from io import StringIO
import importlib.util
import json
from pathlib import Path
import shutil
import tempfile
import unittest
from unittest.mock import patch
import urllib.error

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("public_store_version", ROOT / "scripts/release/Sync-PublicMicrosoftStoreVersion.py")
SYNC = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SYNC)
NOW = datetime(2026, 11, 10, tzinfo=timezone.utc)


def catalog(version="3.2.65535.0"):
    numbers = tuple(map(int, version.split(".")))
    return {"Product": {
        "ProductId": SYNC.PRODUCT_ID, "ProductKind": "Application",
        "Properties": {"PackageIdentityName": SYNC.IDENTITY},
        "DisplaySkuAvailabilities": [{
            "Sku": {"SkuType": "full", "Properties": {"IsTrial": False, "Packages": [{
                "PackageFullName": f"{SYNC.IDENTITY}_{version}_x64__{SYNC.PUBLISHER_ID}",
                "PackageFormat": "Msix", "ResourceId": "",
                "FulfillmentData": {"ProductId": SYNC.PRODUCT_ID},
                "Version": str(sum(part << shift for part, shift in zip(numbers, (48, 32, 16, 0))))
            }]}},
            "Availabilities": [{"Actions": ["Purchase", "Fulfill"], "Conditions": {
                "StartDate": "1753-01-01T00:00:00Z", "EndDate": "9998-12-30T00:00:00Z"
            }}]
        }]
    }}


class PublicStoreVersionTests(unittest.TestCase):
    def test_selects_latest_public_full_package_independently_of_github(self):
        payload = catalog("3.1.65535.0")
        entries = payload["Product"]["DisplaySkuAvailabilities"]
        entries += catalog("3.2.65535.0")["Product"]["DisplaySkuAvailabilities"]
        trial = catalog("9.0.65535.0")["Product"]["DisplaySkuAvailabilities"][0]
        trial["Sku"]["SkuType"] = "trial"
        trial["Sku"]["Properties"]["IsTrial"] = True
        entries.append(trial)
        payload["github_release"] = "v99.0.0"
        self.assertEqual("3.2.65535.0", SYNC.package_version(payload, NOW))

    def test_rejects_wrong_product_or_identity(self):
        for key in ("ProductId", "ProductKind", "PackageIdentityName"):
            payload = catalog()
            target = payload["Product"]["Properties"] if key == "PackageIdentityName" else payload["Product"]
            target[key] = "Wrong"
            with self.subTest(key=key), self.assertRaises(ValueError):
                SYNC.package_version(payload, NOW)

    def test_rejects_unpublished_trial_expired_future_and_preorder_skus(self):
        for scenario in ("trial", "missing-actions", "future", "expired", "preorder"):
            payload = catalog()
            row = payload["Product"]["DisplaySkuAvailabilities"][0]
            availability = row["Availabilities"][0]
            if scenario == "trial":
                row["Sku"]["Properties"]["IsTrial"] = True
            elif scenario == "missing-actions":
                availability["Actions"] = ["Details"]
            elif scenario == "future":
                availability["Conditions"]["StartDate"] = "2027-01-01T00:00:00Z"
            elif scenario == "expired":
                availability["Conditions"]["EndDate"] = "2026-01-01T00:00:00Z"
            else:
                row["Sku"]["Properties"]["IsPreOrder"] = True
            with self.subTest(scenario=scenario), self.assertRaises(ValueError):
                SYNC.package_version(payload, NOW)

    def test_rejects_rc_packages_unknown_version_encoding_and_inconsistent_versions(self):
        for version in ("3.2.1.0", "3.2.65534.0", "3.2.65535.1", "0.2.65535.0"):
            with self.subTest(version=version), self.assertRaises(ValueError):
                SYNC.package_version(catalog(version), NOW)
        payload = catalog()
        payload["Product"]["DisplaySkuAvailabilities"][0]["Sku"]["Properties"]["Packages"][0]["Version"] = "1"
        with self.assertRaises(ValueError):
            SYNC.package_version(payload, NOW)

    def test_rejects_wrong_package_identity_publisher_and_fulfillment_product(self):
        for field, value in (
            ("PackageFullName", "Other_3.2.65535.0_x64__bev6xjqt2jr8t"),
            ("PackageFullName", "Legends77.OTPHarbor_3.2.65535.0_x64__otherpublisher"),
            ("PackageFormat", "Exe"), ("ResourceId", "language-en"), ("FulfillmentData", {"ProductId": "Other"})
        ):
            payload = catalog()
            package = payload["Product"]["DisplaySkuAvailabilities"][0]["Sku"]["Properties"]["Packages"][0]
            package[field] = value
            with self.subTest(field=field, value=value), self.assertRaises(ValueError):
                SYNC.package_version(payload, NOW)

    def fixture(self, directory):
        root = Path(directory)
        for path in ("packaging/windows-store", "scripts/release", "site"):
            (root / path).mkdir(parents=True)
        shutil.copyfile(ROOT / "scripts/release/Set-PublicMicrosoftStoreVersion.ps1",
                        root / "scripts/release/Set-PublicMicrosoftStoreVersion.ps1")
        (root / "packaging/windows-store/public-version.json").write_text(json.dumps({
            "displayVersion": "2.0.1", "packageVersion": "2.1.65535.0"
        }), encoding="utf-8")
        (root / "readme.md").write_text(
            "[![Microsoft Store version](https://img.shields.io/badge/Microsoft%20Store-2.0.1-0078D4?logo=microsoft)]"
            "(https://apps.microsoft.com/detail/9P31KH5L924P)\nOTP Harbor `2.0.1` is publicly available\n"
            "[Desktop](https://img.shields.io/github/v/release/Legends/otp-harbor?filter=v%2A)\n",
            encoding="utf-8")
        (root / "packaging/windows-store/STORE_LISTING.md").write_text("## Version 2.0.1 release notes\n", encoding="utf-8")
        (root / "site/index.html").write_text(
            '<span data-release-channel="store">2.0.1</span>\n'
            '<span data-release-channel="desktop">9.1.0</span>\n'
            '<span data-release-channel="android">8.2.0</span>\n', encoding="utf-8")
        return root

    def snapshot(self, root):
        return {str(path.relative_to(root)): path.read_bytes() for path in root.rglob("*") if path.is_file()}

    def test_sync_updates_all_store_metadata_with_existing_mapping_and_preserves_badge(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self.fixture(directory)
            self.assertEqual("3.2.65535.0", SYNC.sync(catalog(), root))
            data = json.loads((root / "packaging/windows-store/public-version.json").read_text(encoding="utf-8"))
            self.assertEqual({"displayVersion": "3.0.2", "packageVersion": "3.2.65535.0"}, data)
            readme = (root / "readme.md").read_text(encoding="utf-8")
            self.assertIn("Microsoft%20Store-3.2.65535.0-0078D4?logo=microsoft", readme)
            self.assertIn("OTP Harbor package `3.2.65535.0` is publicly available", readme)
            self.assertIn("](https://apps.microsoft.com/detail/9P31KH5L924P)", readme)
            self.assertIn("github/v/release/Legends/otp-harbor?filter=v%2A", readme)
            self.assertEqual(
                '<span data-release-channel="store">3.2.65535.0</span>\n'
                '<span data-release-channel="desktop">9.1.0</span>\n'
                '<span data-release-channel="android">8.2.0</span>\n',
                (root / "site/index.html").read_text(encoding="utf-8"))
            self.assertIn("## Version 3.0.2 release notes",
                          (root / "packaging/windows-store/STORE_LISTING.md").read_text(encoding="utf-8"))
            before = self.snapshot(root)
            SYNC.sync(catalog(), root)
            SYNC.sync(catalog(), root, check=True)
            self.assertEqual(before, self.snapshot(root))

    def test_older_catalog_does_not_overwrite_confirmed_version(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self.fixture(directory)
            (root / "packaging/windows-store/public-version.json").write_text(json.dumps({
                "displayVersion": "3.0.3", "packageVersion": "3.3.65535.0"
            }), encoding="utf-8")
            before = self.snapshot(root)
            with self.assertRaises(ValueError):
                SYNC.sync(catalog(), root)
            self.assertEqual(before, self.snapshot(root))

    def test_network_or_schema_failure_retains_confirmed_files(self):
        failures = (urllib.error.URLError("unavailable"), ValueError("invalid response"))
        with tempfile.TemporaryDirectory() as directory:
            root = self.fixture(directory)
            before = self.snapshot(root)
            for error in failures:
                with patch.object(SYNC, "fetch_catalog", side_effect=error), patch("sys.argv", [
                    "sync", "--repository-root", str(root), "--keep-current-on-error"
                ]), redirect_stdout(StringIO()) as output:
                    SYNC.main()
                self.assertIn("retaining confirmed metadata", output.getvalue())
                self.assertEqual(before, self.snapshot(root))
            with patch.object(SYNC, "fetch_catalog", return_value={}), patch("sys.argv", [
                "sync", "--repository-root", str(root), "--keep-current-on-error"
            ]), redirect_stdout(StringIO()) as output:
                SYNC.main()
            self.assertIn("retaining confirmed metadata", output.getvalue())
            self.assertEqual(before, self.snapshot(root))

    def test_workflow_checks_store_and_stages_all_synchronized_files(self):
        workflow = (ROOT / ".github/workflows/readme-releases.yml").read_text(encoding="utf-8")
        self.assertIn("Sync-PublicMicrosoftStoreVersion.py --keep-current-on-error", workflow)
        self.assertIn("git add -- readme.md site/index.html packaging/windows-store/public-version.json packaging/windows-store/STORE_LISTING.md", workflow)
        self.assertNotIn("github", SYNC.CATALOG_URL.lower())


if __name__ == "__main__":
    unittest.main()
