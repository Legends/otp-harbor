import contextlib
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('historical_audit', ROOT / 'scripts/validation/Audit-HistoricalGitleaks.py')
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)


class HistoricalInventoryTests(unittest.TestCase):
    def finding(self, value, path, line):
        return dict(Secret=value, Match='private matcher context', Description='untrusted description',
                    Author='private author', Email='private email', Commit='a' * 40, File=path,
                    StartLine=line, StartColumn=1, RuleID='generic-api-key',
                    Fingerprint=f'{"a" * 40}:{path}:generic-api-key:{line}')

    def test_deduplicates_values_without_exporting_values_or_raw_context(self):
        rows = [self.finding('unused synthetic fixture one', 'a.json', 1),
                self.finding('unused synthetic fixture one', 'b.json', 2),
                self.finding('unused synthetic fixture two', 'c.json', 3)]
        report = audit.inventory(rows)
        self.assertEqual(3, report['findingCount'])
        self.assertEqual(2, report['distinctDetectedValues'])
        self.assertEqual(['U001', 'U001', 'U002'], [row['valueId'] for row in report['findings']])
        serialized = json.dumps(report)
        self.assertTrue(all(row['Secret'] not in serialized for row in rows), 'An input value escaped redaction.')
        self.assertTrue(all(key not in serialized for key in ('private matcher', 'private author', 'private email', 'untrusted description')))

    def test_metadata_containing_a_value_is_rejected_without_printing_it(self):
        with self.assertRaises(audit.AuditError):
            audit.inventory([self.finding('synthetic-value-in-path', 'synthetic-value-in-path.json', 1)])

    def test_wrong_engine_or_unparseable_output_never_echoes_raw_diagnostics(self):
        for responses in ([subprocess.CompletedProcess([], 0, b'wrong-version', b'')],
                          [subprocess.CompletedProcess([], 0, '8.30.1\n', ''),
                           subprocess.CompletedProcess([], 1, b'unparsed-private-response', b'private-diagnostics')]):
            output = io.StringIO()
            with patch.object(subprocess, 'run', side_effect=responses), contextlib.redirect_stderr(output):
                self.assertEqual(2, audit.cli(['--gitleaks', 'synthetic-binary', '--output', 'unused.json']))
            self.assertNotIn('unparsed-private-response', output.getvalue())
            self.assertNotIn('private-diagnostics', output.getvalue())

    def test_scan_status_is_not_changed_to_success_when_findings_remain(self):
        import tempfile
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / 'inventory.json'
            with patch.object(audit, 'scan', return_value=[self.finding('unused synthetic value', 'a.json', 1)]), \
                    patch.object(audit, 'git', return_value=b'a' * 40 + b'\n'), contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(1, audit.cli(['--output', str(output)]))
            self.assertEqual(1, json.loads(output.read_text())['findingCount'])

    def test_final_git_metadata_is_checked_before_any_report_is_written(self):
        import tempfile
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / 'inventory.json'
            with patch.object(audit, 'scan', return_value=[self.finding('b' * 40, 'a.json', 1)]), \
                    patch.object(audit, 'git', return_value=b'b' * 40 + b'\n'), contextlib.redirect_stderr(io.StringIO()):
                self.assertEqual(2, audit.cli(['--output', str(output)]))
            self.assertFalse(output.exists())


if __name__ == '__main__':
    unittest.main()
