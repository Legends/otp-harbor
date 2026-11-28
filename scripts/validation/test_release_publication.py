import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts/release/shared'))
sys.path.insert(0, str(ROOT / 'scripts/release/windows'))
import github_release as publication
import release_notes
import verify_legacy_assets
import store_monitor


class FakeGitHub:
    def __init__(self):
        self.source = None
        self.release = None
        self.assets = []
        self.calls = []
        self.published_releases = []

    def tagsource(self, tag):
        return self.source

    def api(self, method, path, body=None, missing=False):
        self.calls.append((method, path))
        if method == 'POST' and path == 'git/refs':
            self.source = body['sha']
        elif method == 'POST' and path == 'releases':
            self.release = body | {'id': 1, 'html_url': 'https://github.com/example/release'}
            return self.release
        elif method == 'GET' and path.startswith('releases/tags/'):
            return self.release
        elif method == 'GET' and path.startswith('releases?'):
            return self.published_releases
        elif method == 'GET' and '/assets?' in path:
            return self.assets
        elif method == 'PATCH':
            self.release |= body
            return self.release
        else:
            raise AssertionError((method, path))

    def upload(self, identifier, path):
        self.assets.append(dict(name=path.name, size=path.stat().st_size, digest=publication.sha256(path)))

    def digest(self, asset):
        return asset['digest']


class PublicationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.file = Path(self.temp.name) / 'package.apk'
        self.file.write_bytes(b'synthetic approved package')
        self.github = FakeGitHub()

    def publish(self, body='Approved notes and immutable candidate fingerprint', tag='android-v3.0.7'):
        return publication.publish(self.github, tag, 'a' * 40, [self.file], body, 'Release')

    def test_publishes_verified_draft_and_android_never_becomes_latest(self):
        self.publish()
        self.assertFalse(self.github.release['draft'])
        self.assertEqual('false', self.github.release['make_latest'])
        self.assertEqual('a' * 40, self.github.source)

    def test_identical_published_retry_is_read_only(self):
        self.publish()
        self.github.calls.clear()
        self.publish()
        self.assertTrue(all(method == 'GET' for method, _ in self.github.calls))
        self.assertEqual(1, len(self.github.assets))

    def test_tag_conflict_never_moves_tag(self):
        self.github.source = 'b' * 40
        with self.assertRaises(publication.Conflict):
            self.publish()
        self.assertEqual('b' * 40, self.github.source)

    def test_draft_retry_fills_only_missing_assets(self):
        self.github.source = 'a' * 40
        self.github.release = dict(id=1, tag_name='android-v3.0.7', draft=True, prerelease=False,
                                   body='Approved notes and immutable candidate fingerprint', html_url='https://example.com')
        self.publish()
        self.assertEqual(1, len(self.github.assets))

    def test_changed_candidate_or_existing_assets_fail_without_replacement(self):
        self.publish()
        for change in (lambda: self.file.write_bytes(b'corrupted'), lambda: self.github.assets.append(dict(name='unexpected', size=1, digest='0' * 64))):
            change()
            self.github.calls.clear()
            with self.assertRaises(publication.Conflict):
                self.publish()
            self.assertTrue(all(method == 'GET' for method, _ in self.github.calls))

    def test_competing_request_cannot_adopt_another_candidate(self):
        self.publish()
        with self.assertRaises(publication.Conflict):
            self.publish(body='Different request, same version')

    def test_older_desktop_recovery_cannot_replace_newer_latest(self):
        self.github.published_releases = [dict(tag_name='v3.10.0', draft=False, prerelease=False),
                                          dict(tag_name='android-v99.0.0', draft=False, prerelease=False)]
        self.publish(tag='v3.9.9')
        self.assertEqual('false', self.github.release['make_latest'])

    def test_uncertain_upload_recovers_without_replacing_the_asset(self):
        original = self.github.upload
        def interrupted(identifier, path):
            original(identifier, path)
            raise publication.Conflict('Interrupted response')
        self.github.upload = interrupted
        with self.assertRaises(publication.Conflict):
            self.publish()
        self.github.upload = original
        self.publish()
        self.assertEqual(1, len(self.github.assets))

    def test_legacy_draft_rejects_unexpected_windows_assets_or_hashes(self):
        directory = Path(self.temp.name)
        names = ['OTP-Harbor-android-universal-3.0.7.apk', 'android-release.json', 'release-artifacts-v2.json', 'SHA256SUMS']
        self.github.source = 'a' * 40
        self.github.release = dict(id=1, draft=True, prerelease=False)
        for name in names:
            path = directory / name
            path.write_bytes(b'synthetic release data')
            self.github.assets.append(dict(name=name, size=path.stat().st_size, digest=publication.sha256(path)))
        verify_legacy_assets.verify(self.github, 'android-v3.0.7', 'a' * 40, directory)
        self.github.assets.append(dict(name='unsigned-windows.zip', size=1, digest='0' * 64))
        with self.assertRaises(publication.Conflict):
            verify_legacy_assets.verify(self.github, 'android-v3.0.7', 'a' * 40, directory)


class StoreMonitorTests(unittest.TestCase):
    def record(self, state):
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, 'w') as archive:
            archive.writestr('store-state.json', json.dumps(dict(candidateId='123-1', candidateSha256='a' * 64, submissionId='11', state=state)))
        return stream.getvalue()

    def test_latest_published_status_suppresses_old_pending(self):
        artifacts = [dict(id=2, updated_at='2026-11-28', expired=False, workflow_run={'id': 2}),
                     dict(id=1, updated_at='2026-11-27', expired=False, workflow_run={'id': 1})]
        records = store_monitor.pending_records(artifacts,
            lambda _: dict(head_branch='master', path='.github/workflows/release-reconciliation.yml'),
            lambda identifier: self.record('published' if identifier == 2 else 'certification-pending'),
            lambda run, name: {'id': 99})
        self.assertEqual([], records)

    def test_pending_reconciliation_uses_original_candidate_and_current_state(self):
        records = store_monitor.pending_records([dict(id=2, updated_at='2026-11-28', expired=False, workflow_run={'id': 2})],
            lambda _: dict(head_branch='master', path='.github/workflows/release-reconciliation.yml'),
            lambda _: self.record('certification-pending'), lambda run, name: {'id': 99})
        self.assertEqual('123', records[0]['run_id'])
        self.assertEqual('99', records[0]['artifact_id'])
        self.assertEqual('2', records[0]['state_run_id'])

    def test_untrusted_state_is_not_used(self):
        records = store_monitor.pending_records([dict(id=2, updated_at='2026-11-28', expired=False, workflow_run={'id': 2})],
            lambda _: dict(head_branch='untrusted', path='.github/workflows/release.yml'),
            lambda _: self.record('certification-pending'), lambda run, name: {'id': 99})
        self.assertEqual([], records)


class ReleaseNotesTests(unittest.TestCase):
    def test_notes_include_declared_merged_changes_and_omit_internal_or_unrelated_changes(self):
        releases = [dict(tag_name='v3.0.5', draft=False, prerelease=False)]
        pulls = [dict(number=1, title='Fix QR import', merged_at='2026-11-28', merge_commit_sha='one', labels=[]),
                 dict(number=2, title='Internal build update', merged_at='2026-11-28', merge_commit_sha='two', labels=[{'name': 'ci'}]),
                 dict(number=3, title='Unmerged feature', merged_at=None, merge_commit_sha='three', labels=[])]
        notes = release_notes.render('v3.0.6', 'a' * 40, releases, {'one', 'two'}, pulls)
        self.assertIn('Fix QR import', notes)
        self.assertNotIn('Internal build update', notes)
        self.assertNotIn('Unmerged feature', notes)
        self.assertIn('compare/v3.0.5...' + 'a' * 40, notes)



if __name__ == '__main__':
    unittest.main()
