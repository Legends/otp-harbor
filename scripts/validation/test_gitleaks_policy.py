"""Exercise the historical exception with the same pinned engine as CI."""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
VERSION = '8.30.1'


def engine():
    configured = os.environ.get('GITLEAKS_BINARY') or shutil.which('gitleaks')
    if configured:
        return configured
    cached = Path(tempfile.gettempdir()) / f'gitleaks-{VERSION}' / 'gitleaks'
    if cached.is_file():
        return str(cached)
    raise RuntimeError('The pinned Gitleaks binary is required for policy verification.')


class GitleaksPolicyTests(unittest.TestCase):
    def test_exception_requires_the_reviewed_commit_path_and_rule(self):
        binary = engine()
        version = subprocess.run([binary, 'version'], check=True, capture_output=True, text=True).stdout.strip()
        self.assertEqual(VERSION, version)
        with tempfile.TemporaryDirectory(prefix='otp-harbor-gitleaks-policy-') as directory:
            root = Path(directory)
            repository = root / 'repository'
            repository.mkdir()

            def git(*args):
                return subprocess.run(['git', '-C', str(repository), *args], check=True,
                                      capture_output=True, text=True).stdout.strip()

            def fixture(label):
                return 'api_key = "' + hashlib.sha256(label.encode()).hexdigest() + '"\n'

            git('init')
            git('config', 'user.name', 'Policy fixture')
            git('config', 'user.email', 'fixture@example.invalid')
            reviewed = repository / 'TOTP.Tests/Fixtures/LoadTesting/prominent-platforms-500.json'
            reviewed.parent.mkdir(parents=True)
            reviewed.write_text(fixture('approved historical test data') + 'review_fixture = "' +
                                hashlib.sha256(b'another detector must remain active').hexdigest() + '"\n')
            unrelated = repository / 'unrelated.txt'
            unrelated.write_text(fixture('unrelated path in the reviewed commit'))
            git('add', '.')
            git('commit', '-m', 'Synthetic historical fixture')
            approved_commit = git('rev-parse', 'HEAD')
            reviewed.write_text(fixture('new secret in the historical path'))
            git('add', '.')
            git('commit', '-m', 'Synthetic subsequent change')
            newer_commit = git('rev-parse', 'HEAD')
            configuration = (ROOT / '.gitleaks.toml').read_text()
            configuration = re.sub(r'commits = \["a68522767894e5d72e675c58099339101e483ced"\]',
                                   f'commits = ["{approved_commit}"]', configuration, count=1)
            configuration += '\n[[rules]]\nid = "fixture-other-rule"\n' + \
                'regex = \'\'\'review_fixture = "([a-f0-9]{64})"\'\'\'\nsecretGroup = 1\n'
            config = root / 'gitleaks.toml'
            config.write_text(configuration)
            report = root / 'redacted.json'
            scan = subprocess.run([binary, 'git', str(repository), '--config', str(config),
                                   '--redact', '--report-format', 'json', '--report-path', str(report),
                                   '--no-banner'], capture_output=True, text=True)
            # Never include scanner output or values in an assertion/log.
            self.assertEqual(1, scan.returncode, 'Expected only the three deliberate unapproved findings.')
            findings = json.loads(report.read_text())
            identities = {(item['Commit'], item['File'], item['RuleID']) for item in findings}
            path = reviewed.relative_to(repository).as_posix()
            self.assertNotIn((approved_commit, path, 'generic-api-key'), identities)
            self.assertIn((approved_commit, 'unrelated.txt', 'generic-api-key'), identities)
            self.assertIn((newer_commit, path, 'generic-api-key'), identities)
            self.assertIn((approved_commit, path, 'fixture-other-rule'), identities)
            self.assertEqual(3, len(findings))


if __name__ == '__main__':
    unittest.main()
