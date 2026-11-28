import copy
from contextlib import redirect_stderr
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import MagicMock, Mock, patch
from urllib.error import HTTPError, URLError

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts/release/windows"))
import partner_center as pc


class FakeStore:
    def __init__(self):
        self.calls = []
        self.uploads = []
        self.pending = None
        self.submission = None
        self.creation_error = False
        self.commit_error = False
        self.upload_error = False
        self.published = {"status": "Published", "applicationPackages": [
            {"fileName": "old.msix", "fileStatus": "Uploaded", "architecture": "x64", "version": "3.4.65535.0"},
            {"fileName": "arm.msix", "fileStatus": "Uploaded", "architecture": "ARM64", "version": "3.4.65535.0"}],
            "listings": {"de-de": {"baseListing": {"description": "Existing listing"}}},
            "pricing": {"priceId": "Free", "marketSpecificPricings": {"DE": "Free"}},
            "visibility": "Public", "notesForCertification": "Existing certification notes", "targetPublishMode": "Immediate"}

    def api(self, method, suffix="", body=None):
        self.calls.append((method, suffix))
        if not suffix:
            return dict(id=pc.PRODUCT, packageIdentityName="Legends77.OTPHarbor", publisherName="CN=84095A7C-6458-436E-ABF2-DC02311E25F9",
                        lastPublishedApplicationSubmission={"id": "10"}, pendingApplicationSubmission={"id": self.pending} if self.pending else None)
        if method == "GET" and suffix == "/submissions/10":
            return copy.deepcopy(self.published)
        if method == "POST" and suffix == "/submissions":
            self.pending = "11"
            self.submission = copy.deepcopy(self.published) | {"id": "11", "status": "PendingCommit", "fileUploadUrl": "https://example.blob.core.windows.net/upload?secret=SAS"}
            if self.creation_error:
                raise pc.UncertainMutation("Interrupted")
            return copy.deepcopy(self.submission)
        if method == "PUT":
            self.submission = copy.deepcopy(body)
            return {}
        if suffix == "/submissions/11/commit":
            self.submission["status"] = "Certification"
            if self.commit_error:
                raise pc.UncertainMutation("Interrupted")
            return {}
        if suffix == "/submissions/11":
            return copy.deepcopy(self.submission)
        raise AssertionError((method, suffix))

    def upload(self, url, archive):
        if self.upload_error:
            raise pc.StoreError("Upload unavailable")
        self.uploads.append(archive.read_bytes())


class PartnerCenterTests(unittest.TestCase):
    def test_cli_reports_controlled_conflicts_without_echoing_unparsed_data(self):
        for error, expected in ((pc.StoreError('Unrelated pending submission protected.'), 'Unrelated pending submission'),
                                (ValueError('unparsed-private-response'), 'ValueError')):
            output = io.StringIO()
            with patch.object(pc, 'main', side_effect=error), redirect_stderr(output):
                self.assertEqual(1, pc.cli())
            self.assertIn(expected, output.getvalue())
            self.assertNotIn('unparsed-private-response', output.getvalue())

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.package = self.root / "new.msix"
        self.package.write_bytes(b"Synthetic MSIX; package validation separately tested")
        self.state_path = self.root / "state.json"
        self.metadata = dict(identityName="Legends77.OTPHarbor", publisher="CN=84095A7C-6458-436E-ABF2-DC02311E25F9", version="3.5.65535.0")
        self.state = dict(candidateId="123-1", candidateSha256="a" * 64, packageVersion="3.5.65535.0")
        self.store = FakeStore()

    def submit(self):
        return pc.submit(self.store, self.package, self.metadata, self.state, self.state_path)

    def test_preserves_listing_pricing_and_other_architectures_and_submits(self):
        result = self.submit()
        self.assertEqual("certification-pending", result["state"])
        self.assertEqual("11", result["submissionId"])
        self.assertEqual(self.store.published["listings"], self.store.submission["listings"])
        self.assertEqual(self.store.published["pricing"], self.store.submission["pricing"])
        self.assertEqual("Uploaded", self.store.submission["applicationPackages"][1]["fileStatus"])
        self.assertEqual("PendingDelete", self.store.submission["applicationPackages"][0]["fileStatus"])
        self.assertEqual(1, len(self.store.uploads))
        self.assertNotIn("SAS", self.state_path.read_text())
        self.assertNotIn("fileUploadUrl", self.state_path.read_text())

    def test_unrelated_pending_submission_protected(self):
        self.store.pending = "99"
        with self.assertRaises(pc.StoreError):
            self.submit()
        self.assertFalse(any(method != "GET" for method, _ in self.store.calls))

    def test_published_version_conflict_blocks_creation(self):
        self.metadata["version"] = "3.4.65535.0"
        with self.assertRaises(pc.StoreError):
            self.submit()
        self.assertFalse(any(method == "POST" for method, _ in self.store.calls))

    def test_creation_uncertainty_is_recorded_and_never_retried_or_adopted(self):
        self.store.creation_error = True
        with self.assertRaises(pc.UncertainMutation):
            self.submit()
        self.assertTrue(json.loads(self.state_path.read_text())["creationUncertain"])
        with self.assertRaises(pc.StoreError):
            self.submit()
        self.assertEqual(1, self.store.calls.count(("POST", "/submissions")))

    def test_commit_uncertainty_reconciles_without_double_commit(self):
        self.store.commit_error = True
        with self.assertRaises(pc.UncertainMutation):
            self.submit()
        self.assertEqual("certification-pending", self.state["state"])
        self.submit()
        self.assertEqual(1, self.store.calls.count(("POST", "/submissions/11/commit")))

    def test_upload_failure_does_not_commit_and_same_submission_can_resume(self):
        self.store.upload_error = True
        with self.assertRaises(pc.StoreError):
            self.submit()
        self.assertNotIn(("POST", "/submissions/11/commit"), self.store.calls)
        self.store.upload_error = False
        self.submit()
        self.assertEqual(1, self.store.calls.count(("POST", "/submissions")))

    def test_resumed_upload_uses_current_server_upload_uri(self):
        original = self.store.api
        refreshed = 'https://example.blob.core.windows.net/refreshed?synthetic=not-a-credential'

        def api(method, suffix='', body=None):
            result = original(method, suffix, body)
            if method == 'PUT':
                self.store.submission['fileUploadUrl'] = refreshed
            return result

        self.store.api = api
        self.store.upload = Mock(wraps=self.store.upload)
        self.submit()
        self.assertEqual(refreshed, self.store.upload.call_args.args[0])

    def test_remote_submission_change_blocks_upload_and_commit(self):
        original = self.store.api

        def api(method, suffix='', body=None):
            result = original(method, suffix, body)
            if method == 'PUT':
                self.store.submission['notesForCertification'] = 'Changed by another operation'
            return result

        self.store.api = api
        with self.assertRaises(pc.StoreError):
            self.submit()
        self.assertEqual([], self.store.uploads)
        self.assertNotIn(('POST', '/submissions/11/commit'), self.store.calls)

    def test_state_mapping_and_published_version_validation(self):
        self.submit()
        for remote, expected in (("CertificationFailed", "certification-failed"), ("PendingPublication", "awaiting-publication"),
                                 ("CommitStarted", "submitted"), ("PublishFailed", "failed"), ("NewUnknownState", "unknown")):
            with self.subTest(remote=remote):
                self.store.submission["status"] = remote
                self.assertEqual(expected, pc.reconcile(self.store, self.state)["state"])
        self.store.submission["status"] = "Published"
        with self.assertRaises(pc.StoreError):
            pc.reconcile(self.store, self.state)
        self.store.submission["applicationPackages"].append(dict(version="3.5.65535.0", architecture="x64", fileStatus="Uploaded"))
        self.assertEqual("published", pc.reconcile(self.store, self.state)["state"])

    def test_credentials_and_versions_fail_closed(self):
        with self.assertRaises(pc.StoreError):
            pc.Store("", "", "")
        for value in ("1.2.3.4", "0.0.0.0", "65536.0.0.0", "1.2.3", "1.2.3.0\n"):
            with self.subTest(value=value), self.assertRaises(pc.StoreError):
                pc.package_version(value)

    def test_retry_after_and_bounded_retries(self):
        self.assertGreaterEqual(pc.retry_delay({"Retry-After": "3"}, 0), 3)
        with self.assertRaises(pc.StoreError):
            pc.retry_delay({"Retry-After": "3600"}, 0)
        http = pc.Http(sleep=Mock())
        http.opener = Mock()
        http.opener.open.side_effect = [HTTPError("https://secret", 429, "ignored", {"Retry-After": "0"}, io.BytesIO()), URLError("secret"), URLError("secret"), URLError("secret")]
        with self.assertRaisesRegex(pc.StoreError, "bounded retries"):
            http.request("GET", "https://example.com")
        self.assertEqual(4, http.opener.open.call_count)
        self.assertEqual(3, http.sleep.call_count)

    def test_non_idempotent_http_is_never_blindly_retried(self):
        http = pc.Http(sleep=Mock())
        http.opener = Mock()
        http.opener.open.side_effect = URLError("secret upload URL")
        with self.assertRaises(pc.UncertainMutation) as error:
            http.request("POST", "https://example.com")
        self.assertNotIn("secret", str(error.exception))
        self.assertEqual(1, http.opener.open.call_count)

    def test_expired_read_authentication_refreshes_once(self):
        http = Mock()
        http.request.side_effect = [(b'{"access_token":"synthetic-one"}', {}), pc.StoreError('Remote operation failed with HTTP 401.'),
                                    (b'{"access_token":"synthetic-two"}', {}), (b'{}', {})]
        store = pc.Store('a' * 36, 'synthetic-client', 'synthetic-secret', http=http)
        self.assertEqual({}, store.api('GET'))
        self.assertEqual(4, http.request.call_count)

    def test_authentication_failure_does_not_reach_submission(self):
        http = Mock()
        http.request.side_effect = pc.StoreError('Remote operation failed with HTTP 403.')
        store = pc.Store('a' * 36, 'synthetic-client', 'synthetic-secret', http=http)
        with self.assertRaises(pc.StoreError):
            store.api('GET')
        self.assertEqual(1, http.request.call_count)

    def test_safe_get_retries_transient_503_then_succeeds(self):
        http = pc.Http(sleep=Mock())
        http.opener = Mock()
        response = MagicMock()
        response.__enter__.return_value = response
        response.read.return_value = b'{}'
        response.headers = {}
        http.opener.open.side_effect = [HTTPError('https://example.com', 503, 'unavailable', {}, io.BytesIO()), response]
        self.assertEqual(b'{}', http.request('GET', 'https://example.com')[0])
        self.assertEqual(2, http.opener.open.call_count)
        http.sleep.assert_called_once()

    def test_upload_verifies_blob_length_checksum_and_destination(self):
        http = Mock()
        archive = self.root / 'upload.zip'
        archive.write_bytes(b'synthetic package archive')
        def request(method, url, headers=None, data=None, **kwargs):
            if method == 'PUT':
                self.upload_headers = headers
                return b'', {}
            return b'', {'Content-Length': str(archive.stat().st_size), 'Content-MD5': self.upload_headers['Content-MD5']}
        http.request.side_effect = request
        store = pc.Store('a' * 36, 'synthetic-client', 'synthetic-secret', http=http)
        store.upload('https://example.blob.core.windows.net/upload?sig=synthetic', archive)
        with self.assertRaises(pc.StoreError):
            store.upload('https://untrusted.example/upload', archive)
        http.request.side_effect = [(b'', {}), (b'', {'Content-Length': '1', 'Content-MD5': 'wrong'})]
        with self.assertRaises(pc.StoreError):
            store.upload('https://example.blob.core.windows.net/upload', archive)


if __name__ == "__main__":
    unittest.main()
