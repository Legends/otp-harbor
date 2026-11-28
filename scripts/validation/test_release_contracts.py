import io
import importlib.util
import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
import sys
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts/release/shared'))
import delivery_metrics

spec = importlib.util.spec_from_file_location('restore_fdroid', ROOT / 'scripts/release/android/Restore-FDroidRepository.py')
restore = importlib.util.module_from_spec(spec)
spec.loader.exec_module(restore)


@unittest.skipUnless(shutil.which('pwsh'), 'PowerShell release tooling is unavailable')
class ScriptContractTests(unittest.TestCase):
    def run_script(self, name, *args):
        return subprocess.run(['pwsh', '-NoProfile', '-File', str(ROOT / name), *args], capture_output=True, text=True, encoding='utf-8')

    def test_actual_gate_rejects_failure_skip_cancel_and_accepts_intentional_skip(self):
        for value, expected in [('success', 0), ('failure', 1), ('skipped', 1), ('cancelled', 1)]:
            with self.subTest(value=value):
                result = self.run_script('scripts/release/shared/Test-ReleaseGate.ps1', '-NeedsJson',
                    json.dumps({'shared': {'result': value}, 'unrelated': {'result': 'skipped'}}), '-Required', 'shared', '-Optional', 'unrelated')
                self.assertEqual(expected, result.returncode, result.stderr)

    def test_existing_store_mapping_and_read_only_inverse_do_not_change_metadata(self):
        result = self.run_script('scripts/release/windows/Get-MicrosoftStoreVersion.ps1', '-ReleaseTag', 'v3.2.7')
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual('3.2007.65535.0', result.stdout.strip())
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            sentinel = root / 'unchanged.txt'
            sentinel.write_text('unchanged')
            result = self.run_script('scripts/release/windows/Set-PublicMicrosoftStoreVersion.ps1', '-PackageVersion', '3.2007.65535.0', '-ResolveOnly', '-RepositoryRoot', directory)
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertEqual('3.2.7', result.stdout.strip())
            self.assertEqual(['unchanged.txt'], [p.name for p in root.iterdir()])
            self.assertEqual('unchanged', sentinel.read_text())
        result = self.run_script('scripts/release/windows/Get-MicrosoftStoreVersion.ps1', '-ReleaseTag', 'v3.0.1000')
        self.assertNotEqual(0, result.returncode)

    def test_existing_android_upgrade_mapping_is_monotonic_and_bounded(self):
        values = []
        for tag in ('android-v3.0.6', 'android-v3.0.7', 'android-v3.1.0'):
            result = self.run_script('scripts/release/android/Get-AndroidReleaseVersion.ps1', '-ReleaseTag', tag)
            self.assertEqual(0, result.returncode, result.stderr)
            metadata = json.loads(result.stdout)
            self.assertEqual('io.github.legends.otpharbor', metadata['applicationId'])
            values.append(metadata['versionCode'])
        self.assertEqual([30000699, 30000799, 30100099], values)
        self.assertNotEqual(0, self.run_script('scripts/release/android/Get-AndroidReleaseVersion.ps1', '-ReleaseTag', 'android-v210.0.0').returncode)

    def test_downloaded_archive_cannot_supply_verification_code(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            injected = root / 'scripts/release/shared/Test-CandidateOrigin.ps1'
            injected.parent.mkdir(parents=True)
            injected.write_text("throw 'Injected verification script executed'", encoding='utf-8')
            (root / 'candidate.json').write_text('{}')
            (root / 'candidate-payload').mkdir()
            result = subprocess.run(['pwsh', '-NoProfile', '-File',
                str(ROOT / 'scripts/release/shared/Test-CandidateOrigin.ps1'),
                '-RunId', '1', '-ArtifactId', '1', '-Digest', 'a' * 64], cwd=root,
                env=os.environ | {'GITHUB_REPOSITORY': 'Legends/otp-harbor', 'GITHUB_REF': 'refs/heads/master'},
                capture_output=True, text=True)
            self.assertNotEqual(0, result.returncode)
            self.assertIn('Unexpected candidate archive entries', result.stderr)
            self.assertNotIn('Injected verification script executed', result.stderr)

    def test_store_output_rejects_a_sibling_of_the_artifacts_directory(self):
        result = self.run_script('scripts/release/windows/New-MicrosoftStoreMsix.ps1',
            '-IdentityName', 'Legends77.OTPHarbor', '-Publisher', 'CN=Synthetic',
            '-PublisherDisplayName', 'Synthetic', '-Version', '3.6.65535.0',
            '-OutputDirectory', 'artifacts-outside/forbidden')
        self.assertNotEqual(0, result.returncode)
        self.assertIn('must remain below the repository artifacts directory', result.stderr)
        self.assertFalse((ROOT / 'artifacts-outside').exists())


class DeliveryContractTests(unittest.TestCase):
    def test_privileged_jobs_extract_data_away_from_trusted_verification_code(self):
        for name in ('reusable-promote.yml', 'reusable-store-submission.yml'):
            workflow = (ROOT / '.github/workflows' / name).read_text()
            self.assertIn('working-directory: release-input', workflow)
            self.assertIn('path: release-input', workflow)
            self.assertNotIn('path: .\n', workflow)
            self.assertIn('$env:GITHUB_WORKSPACE/scripts/release/shared/Test-CandidateOrigin.ps1', workflow)

    def test_external_actions_are_immutable_and_pr_context_is_unprivileged(self):
        import re
        files = list((ROOT / '.github/workflows').glob('*.yml')) + list((ROOT / '.github/actions').rglob('action.yml'))
        for path in files:
            text = path.read_text(encoding='utf-8')
            for action in re.findall(r'uses: ([^\s]+)', text):
                if not action.startswith('./'):
                    self.assertRegex(action, r'@[0-9a-f]{40}$', str(path))
            self.assertNotIn('pull_request_target:', text, str(path))

    def test_production_publication_is_after_combined_candidate_and_protected(self):
        import re
        release = (ROOT / '.github/workflows/release.yml').read_text(encoding='utf-8')
        for job in ('publish_desktop', 'publish_android', 'store'):
            section = re.split(r'\n  (?=\S)', release.split('  ' + job + ':\n', 1)[1], maxsplit=1)[0]
            self.assertIn('needs: [plan, candidate]', section)
            self.assertIn("inputs.mode != 'validate'", section)
        promotion = (ROOT / '.github/workflows/reusable-promote.yml').read_text(encoding='utf-8')
        self.assertIn("environment: ${{ inputs.mode == 'automatic' && 'release-automatic' || 'release-production' }}", promotion)
        self.assertIn('Test-CandidateOrigin.ps1', promotion)
        self.assertIn('candidate.plan.mode -cne', promotion)
        self.assertNotIn('--clobber', (ROOT / 'scripts/release/shared/github_release.py').read_text(encoding='utf-8').replace('# No --clobber.', ''))

    def test_trigger_propagation_and_concurrency_do_not_depend_on_tag_events(self):
        release = (ROOT / '.github/workflows/release.yml').read_text(encoding='utf-8')
        self.assertIn('uses: ./.github/workflows/reusable-desktop-package.yml', release)
        self.assertIn('uses: ./.github/workflows/reusable-android-package.yml', release)
        self.assertIn('uses: ./.github/workflows/readme-releases.yml', release)
        for name in ('release.yml', 'release-recovery.yml'):
            text = (ROOT / '.github/workflows' / name).read_text(encoding='utf-8')
            self.assertIn('group: otp-production-release', text)
            self.assertIn('cancel-in-progress: false', text)
        refresh = (ROOT / '.github/workflows/readme-releases.yml').read_text(encoding='utf-8')
        self.assertIn('gh workflow run pages.yml', refresh)

    def test_metrics_keep_unobserved_dora_values_unknown(self):
        report = delivery_metrics.measure(
            [dict(id=1, path='.github/workflows/build-and-test.yml', event='pull_request', status='completed', conclusion='failure', created_at='2026-11-28T10:00:00Z')],
            [dict(tag_name='android-v3.0.7', draft=False, prerelease=False, published_at='2026-11-28T11:00:00Z')],
            {'1': [dict(started_at='2026-11-28T10:00:03Z', completed_at='2026-11-28T10:02:00Z', conclusion='failure')]})
        self.assertEqual(3, report['ci']['runs'][0]['queueSeconds'])
        self.assertEqual(117, report['ci']['runs'][0]['executionSeconds'])
        self.assertEqual(120, report['ci']['runs'][0]['firstFailedGateSeconds'])
        self.assertIsNone(report['channels']['android']['changeFailureRate'])
        self.assertIsNone(report['channels']['android']['changeLeadTimeSeconds'])

    def test_fallback_archive_rejects_escape_symlink_and_private_material(self):
        for name, mode in [('../escape', 0), ('/absolute', 0), ('C:/drive', 0), ('dir\\escape', 0), ('link', stat.S_IFLNK), ('private.jks', 0)]:
            stream = io.BytesIO()
            with zipfile.ZipFile(stream, 'w') as archive:
                item = zipfile.ZipInfo(name)
                item.external_attr = mode << 16
                archive.writestr(item, 'synthetic')
            data = stream.getvalue()
            if name == 'dir\\escape':
                # ZipInfo normalizes the Windows separator when constructing a
                # fixture; restore the raw malicious archive name in both headers.
                data = data.replace(b'dir/escape', b'dir\\escape')
            with self.subTest(name=name), tempfile.TemporaryDirectory() as directory, self.assertRaises(ValueError):
                restore.extract_public(data, Path(directory))


if __name__ == '__main__':
    unittest.main()
