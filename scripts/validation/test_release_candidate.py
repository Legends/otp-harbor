import json
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts/release/shared"))
import release_candidate as candidate
from release_plan import make_plan


class CandidateTests(unittest.TestCase):
    def test_test_evidence_rejects_empty_skipped_incomplete_failed_or_unrelated_runs(self):
        valid = dict(sourceCommit='a' * 40, suite='full-integration', counts=dict(total=3, passed=2, failed=0, skipped=1))
        candidate.validate_test_results(valid, 'a' * 40)
        for change in (dict(sourceCommit='b' * 40), dict(suite='filtered-pr'), dict(counts={}),
                       dict(counts=dict(total=3, passed=2, failed=0, skipped=0)),
                       dict(counts=dict(total=3, passed=0, failed=0, skipped=3)),
                       dict(counts=dict(total=3, passed=1, failed=1, skipped=1))):
            with self.subTest(change=change), self.assertRaises(ValueError):
                candidate.validate_test_results(valid | change, 'a' * 40)

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "payload"
        self.root.mkdir()

    def write(self, name, contents="synthetic"):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(contents, encoding="utf-8")
        return path

    def test_paths_reject_escape_absolute_backslash_drive_and_missing(self):
        for name in ("../secret", "/outside", "C:/outside", "dir\\file", "dir/../file", "missing", "./file"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                candidate.safe_file(self.root, name)

    def test_immutable_digest_and_file_set(self):
        self.write("android/validation.apk")
        plan = make_plan(scope="mobile", desktop="auto", android="1.0.0", mode="validate", store_action="prepare",
                         source="a" * 40, ref="refs/heads/master", occupied=[], run_id="123", attempt="1")
        metadata = candidate.create_candidate(plan, self.root, [{"id": 123}], {"shared": "success"}, None, None, "",
            dict(sourceCommit='a' * 40, suite='full-integration', counts=dict(total=7, passed=7, failed=0, skipped=0)))
        manifest = Path(self.temp.name) / "candidate.json"
        manifest.write_text(json.dumps(metadata))
        digest = candidate.sha256(manifest)
        candidate.verify_candidate(metadata, self.root, digest, manifest, production=False)
        with self.assertRaises(ValueError):
            candidate.verify_candidate(metadata, self.root, digest, manifest)
        for change in (lambda: self.write("android/validation.apk", "modified"), lambda: self.write("extra")):
            change()
            with self.assertRaises(ValueError):
                candidate.verify_candidate(metadata, self.root, digest, manifest, production=False)
        with self.assertRaises(ValueError):
            candidate.verify_candidate(metadata, self.root, "0" * 64, manifest, production=False)

    def test_msix_actual_manifest_and_metadata(self):
        msix = self.root / "store/package.msix"
        msix.parent.mkdir()
        identity = dict(Name="Legends77.OTPHarbor", Publisher="CN=84095A7C-6458-436E-ABF2-DC02311E25F9", Version="3.5.65535.0", ProcessorArchitecture="x64")
        with zipfile.ZipFile(msix, "w") as archive:
            archive.writestr("AppxManifest.xml", '<Package xmlns="urn:appx"><Identity ' + ' '.join(f'{k}="{v}"' for k, v in identity.items()) + '/></Package>')
        metadata = dict(schemaVersion=1, package="package.msix", identityName=identity["Name"], publisher=identity["Publisher"], version=identity["Version"], architecture="x64", distribution="microsoft-store-only", signed=False, sha256=candidate.sha256(msix))
        self.write("store/store-package.json", json.dumps(metadata))
        candidate.check_msix(self.root, "3.5.65535.0")
        for key, value in (("publisher", "CN=other"), ("version", "3.4.65535.0"), ("architecture", "arm64"), ("sha256", "0" * 64)):
            self.write("store/store-package.json", json.dumps(metadata | {key: value}))
            with self.subTest(key=key), self.assertRaises(ValueError):
                candidate.check_msix(self.root, "3.5.65535.0")

    def test_android_certificate_version_and_hash(self):
        apk = self.write("android/OTP-Harbor-android-universal-3.0.7.apk")
        metadata = dict(applicationId="io.github.legends.otpharbor", displayVersion="3.0.7", versionCode=30000799,
                        certificateSha256="a" * 64, sha256=candidate.sha256(apk), fileName=apk.name)
        self.write("android/android-release.json", json.dumps(metadata))
        candidate.check_android(self.root, "3.0.7", 30000799, "a" * 64)
        for key, value in (("applicationId", "other.app"), ("versionCode", 30000699), ("certificateSha256", "b" * 64), ("sha256", "0" * 64)):
            self.write("android/android-release.json", json.dumps(metadata | {key: value}))
            with self.subTest(key=key), self.assertRaises(ValueError):
                candidate.check_android(self.root, "3.0.7", 30000799, "a" * 64)


if __name__ == "__main__":
    unittest.main()
