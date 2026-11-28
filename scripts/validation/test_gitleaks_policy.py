"""Exercise the historical exception with the same pinned engine as CI."""
import hashlib
import importlib.util
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
    def test_reviewed_commit_fingerprint_does_not_hide_other_lines_paths_rules_or_future_commits(self):
        spec = importlib.util.spec_from_file_location('historical_audit_policy', ROOT / 'scripts/validation/Audit-HistoricalGitleaks.py')
        audit = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(audit)
        with tempfile.TemporaryDirectory(prefix='otp-gitleaks-fingerprint-') as directory:
            root = Path(directory)
            repository = root / 'repository'
            repository.mkdir()

            def git(*args):
                result = subprocess.run(['git', '-C', str(repository), *args], capture_output=True, text=True)
                self.assertEqual(0, result.returncode, 'Fixture Git operation failed; raw output suppressed.')
                return result.stdout.strip()

            def fixture(label, name='api_key'):
                return name + ' = "' + hashlib.sha256(label.encode()).hexdigest() + '"\n'

            git('init')
            git('config', 'user.name', 'Policy fixture')
            git('config', 'user.email', 'fixture@example.invalid')
            reviewed = repository / 'fixture.txt'
            second_line = fixture('other line must stay detected', 'other_api_key')
            reviewed.write_text(fixture('reviewed synthetic value') + second_line)
            (repository / 'unrelated.txt').write_text(fixture('different path must stay detected'))
            git('add', '.')
            git('commit', '-m', 'Synthetic reviewed fixture')
            approved_commit = git('rev-parse', 'HEAD')
            reviewed.write_text(fixture('future commit must stay detected') + second_line)
            git('add', '.')
            git('commit', '-m', 'Synthetic subsequent change')
            newer_commit = git('rev-parse', 'HEAD')
            configuration = root / 'gitleaks.toml'
            configuration.write_text((ROOT / '.gitleaks.toml').read_text())
            before = audit.scan(repository, configuration, engine())
            fingerprint = f'{approved_commit}:fixture.txt:generic-api-key:1'
            self.assertIn(fingerprint, {row['Fingerprint'] for row in before})
            ignored = root / '.gitleaksignore'
            ignored.write_text('# One reviewed immutable location only.\n' + fingerprint + '\n')
            after = audit.scan(repository, configuration, engine(), ignored)
            identities = {(row['Commit'], row['File'], row['RuleID'], row['StartLine']) for row in after}
            self.assertEqual({row['Fingerprint'] for row in before} - {fingerprint}, {row['Fingerprint'] for row in after})
            self.assertIn((approved_commit, 'fixture.txt', 'generic-api-key', 2), identities)
            self.assertIn((approved_commit, 'unrelated.txt', 'generic-api-key', 1), identities)
            self.assertIn((newer_commit, 'fixture.txt', 'generic-api-key', 1), identities)
            # Isolate the second detector: overlapping rules otherwise compete
            # during native deduplication and do not test fingerprint matching.
            other_configuration = root / 'other-rule.toml'
            other_configuration.write_text('[[rules]]\nid = "fixture-other-rule"\n'
                'regex = \'\'\'api_key = "([a-f0-9]{64})"\'\'\'\nsecretGroup = 1\n')
            other = audit.scan(repository, other_configuration, engine(), ignored)
            self.assertIn((approved_commit, 'fixture.txt', 'fixture-other-rule', 1),
                          {(row['Commit'], row['File'], row['RuleID'], row['StartLine']) for row in other})

    def test_every_exception_has_review_evidence_and_unresolved_values_remain_detectable(self):
        review = json.loads((ROOT / 'docs/security/evidence/historical-gitleaks-review-20261128.json').read_text())
        findings = review['findings']
        self.assertEqual(46, len(findings))
        permitted = {'verified-test-fixture', 'verified-crypto-fixture',
                     'authorized-indexnow-protocol-key', 'owner-attested-synthetic-totp-fixture'}
        expected = {row['fingerprint'] for row in findings if row['classification'] in permitted}
        entries = [line.strip() for line in (ROOT / '.gitleaksignore').read_text().splitlines()
                   if line.strip() and not line.lstrip().startswith('#')]
        for entry in entries:
            self.assertTrue(re.fullmatch(r'[0-9a-f]{40}:[^:\r\n]+:[a-z0-9-]+:[1-9][0-9]*', entry) is not None,
                            'Exceptions must bind immutable commits, paths, rules and lines; raw entry suppressed.')
        self.assertEqual(len(entries), len(set(entries)))
        self.assertEqual(expected, set(entries))
        self.assertEqual(46, len(entries))
        self.assertEqual(42, sum(row['classification'] in {'verified-test-fixture', 'verified-crypto-fixture'}
                                 for row in findings))
        self.assertTrue(all(row['evidence'] for row in findings), 'Every classification needs recorded evidence.')
        self.assertTrue(all(row['fingerprint'] not in entries for row in findings
                            if row['classification'] not in permitted), 'Unreviewed findings must remain blocking.')
        protocol = [row for row in findings if row['classification'] == 'authorized-indexnow-protocol-key']
        self.assertEqual({'F001', 'F046'}, {row['id'] for row in protocol})
        attested = [row for row in findings if row['classification'] == 'owner-attested-synthetic-totp-fixture']
        self.assertEqual({'F002', 'F042'}, {row['id'] for row in attested})
        provenance = json.loads((ROOT / 'docs/security/evidence/historical-provenance-20261128.json').read_text())
        self.assertTrue(provenance['indexNow']['ownerProtocolExceptionAuthorized'])
        self.assertTrue(provenance['indexNow']['allPathsClassified'])
        self.assertTrue(provenance['indexNow']['verificationFileMatches'])
        self.assertFalse(provenance['indexNow']['otherCredentialReuseObserved'])
        self.assertTrue(provenance['legacyTotp']['ownerAttestation'])
        self.assertFalse(provenance['legacyTotp']['authenticationOrCodeGenerationAttempted'])

    def test_protocol_exceptions_preserve_unrelated_credentials_and_same_value_elsewhere(self):
        spec = importlib.util.spec_from_file_location('protocol_audit_policy', ROOT / 'scripts/validation/Audit-HistoricalGitleaks.py')
        audit = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(audit)
        review = json.loads((ROOT / 'docs/security/evidence/historical-gitleaks-review-20261128.json').read_text())
        approved = [row for row in review['findings'] if row['id'] in {'F001', 'F046'}]
        with tempfile.TemporaryDirectory(prefix='otp-protocol-exception-') as directory:
            repository = Path(directory) / 'repository'
            repository.mkdir()

            def git(*args):
                result = subprocess.run(['git', '-C', str(repository), *args], capture_output=True, text=True)
                self.assertEqual(0, result.returncode, 'Synthetic Git operation failed; diagnostics suppressed.')
                return result.stdout.strip()

            # Generated stand-ins only. Never place the actual protocol value in
            # this test, fixture Git repository, command arguments or disk report.
            value = hashlib.sha256(b'synthetic protocol stand-in').hexdigest()
            other = hashlib.sha256(b'unrelated credential must remain detected').hexdigest()
            for row in approved:
                path = repository / row['path']
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text('\n' * (row['line'] - 1) + f'api_key = "{value}"\n' + f'other_api_key = "{other}"\n')
            (repository / 'unrelated.txt').write_text(f'api_key = "{value}"\n')
            git('init'); git('config', 'user.name', 'Policy fixture')
            git('config', 'user.email', 'fixture@example.invalid')
            git('add', '.'); git('commit', '-m', 'Synthetic protocol fixture')
            commit = git('rev-parse', 'HEAD')
            ignored = Path(directory) / '.gitleaksignore'
            fingerprints = {f"{commit}:{row['path']}:{row['rule']}:{row['line']}" for row in approved}
            ignored.write_text('\n'.join(sorted(fingerprints)) + '\n')
            before = audit.scan(repository, ROOT / '.gitleaks.toml', engine())
            after = audit.scan(repository, ROOT / '.gitleaks.toml', engine(), ignored)
            self.assertTrue(fingerprints <= {row['Fingerprint'] for row in before})
            self.assertEqual({row['Fingerprint'] for row in before} - fingerprints,
                             {row['Fingerprint'] for row in after})
            self.assertEqual(3, len(after))
            self.assertIn('unrelated.txt', {row['File'] for row in after})
            for row in approved:
                self.assertIn((row['path'], row['line'] + 1), {(hit['File'], hit['StartLine']) for hit in after})

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
