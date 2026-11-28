"""Export value-free Gitleaks 8.30.1 inventory; plaintext stays in captured process memory.

The native scanner's nonzero finding status is preserved. This is an audit helper,
not a replacement for the release pipeline's Gitleaks gate.
"""
import argparse
from collections import Counter
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile

VERSION = '8.30.1'
ROOT = Path(__file__).resolve().parents[2]


class AuditError(RuntimeError):
    pass


def git(repository, *arguments):
    result = subprocess.run(['git', '-C', str(repository), *arguments], capture_output=True)
    if result.returncode:
        raise AuditError('Git evidence could not be read; raw output suppressed.')
    return result.stdout


def engine_path(configured=None):
    if configured or os.environ.get('GITLEAKS_BINARY'):
        return configured or os.environ['GITLEAKS_BINARY']
    installed = shutil.which('gitleaks')
    cached = Path(tempfile.gettempdir()) / f'gitleaks-{VERSION}' / 'gitleaks'
    if installed or cached.is_file():
        return installed or str(cached)
    raise AuditError('Install the official Gitleaks 8.30.1 binary or set GITLEAKS_BINARY.')


def scan(repository, configuration, binary=None, ignore_path=None):
    binary = engine_path(binary)
    version = subprocess.run([str(binary), 'version'], capture_output=True, text=True)
    if version.returncode or version.stdout.strip() != VERSION:
        raise AuditError('The release pipeline requires exactly Gitleaks 8.30.1.')
    arguments = [str(binary), 'git', str(Path(repository).resolve()),
                 '--config', str(Path(configuration).resolve()), '--exit-code', '1',
                 '--report-format', 'json', '--report-path', '-', '--no-banner', '--log-level', 'fatal']
    if ignore_path is not None:
        arguments += ['--gitleaks-ignore-path', str(Path(ignore_path).resolve())]
    # Upstream report.StdoutReportPath="-" writes to the captured pipe. Never
    # forward raw stdout/stderr, use a disk report, or place values in arguments.
    with tempfile.TemporaryDirectory(prefix='otp-gitleaks-process-') as directory:
        result = subprocess.run(arguments, cwd=directory, capture_output=True, timeout=300)
    if result.returncode not in (0, 1):
        raise AuditError('Gitleaks execution failed; raw output suppressed.')
    try:
        findings = json.loads(result.stdout)
    except (ValueError, UnicodeError):
        raise AuditError('Gitleaks output was invalid; raw output suppressed.') from None
    if findings is None:
        findings = []
    if not isinstance(findings, list) or bool(findings) != (result.returncode == 1):
        raise AuditError('Scanner result and exit status disagree.')
    return findings


def inventory(findings, containing_refs=None):
    ordered = sorted(findings, key=lambda r: (r['File'], r['Commit'], r['StartLine'], r['StartColumn']))
    values = {}
    for row in ordered:
        if not isinstance(row.get('Secret'), str) or not row['Secret']:
            raise AuditError('A finding has no usable value for in-memory deduplication.')
        values.setdefault(row['Secret'], f'U{len(values) + 1:03}')
    counts = Counter(row['Secret'] for row in ordered)
    rows = []
    for number, row in enumerate(ordered, 1):
        if not re.fullmatch('[0-9a-f]{40}', row['Commit']):
            raise AuditError('Invalid finding commit.')
        rows.append(dict(id=f'F{number:03}', valueId=values[row['Secret']], rule=row['RuleID'],
                         path=row['File'], commit=row['Commit'], line=row['StartLine'],
                         fingerprint=row['Fingerprint'], sameValueFindingCount=counts[row['Secret']]))
    result = dict(schemaVersion=1, engineVersion=VERSION, findingCount=len(rows),
                  distinctDetectedValues=len(values), findings=rows)
    if containing_refs is not None:
        result['containingRefs'] = containing_refs
    serialized = json.dumps(result, ensure_ascii=True)
    if any(value in serialized for value in values):
        raise AuditError('Output redaction guard rejected a value in metadata.')
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repository', type=Path, default=ROOT)
    parser.add_argument('--configuration', type=Path, default=ROOT / '.gitleaks.toml')
    parser.add_argument('--gitleaks')
    parser.add_argument('--ignore-path', type=Path, help='Explicit reviewed ignore file; source-local ignores also apply.')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args(argv)
    ignore = args.ignore_path
    if ignore is None and (args.configuration.parent / '.gitleaksignore').is_file():
        ignore = args.configuration.parent / '.gitleaksignore'
    findings = scan(args.repository, args.configuration, args.gitleaks, ignore)
    commits = sorted({row['Commit'] for row in findings})
    refs = {commit: git(args.repository, 'for-each-ref', '--contains=' + commit, '--format=%(refname)').decode().splitlines()
            for commit in commits}
    report = inventory(findings, refs)
    report['repositoryHead'] = git(args.repository, 'rev-parse', 'HEAD').decode().strip()
    report['configurationSha256'] = hashlib.sha256(args.configuration.read_bytes()).hexdigest()
    report['scope'] = 'native git --full-history --all --diff-filter=tuxdb'
    report['opaqueIdScope'] = 'This inventory only; compare separate scans by finding fingerprints.'
    serialized = json.dumps(report, indent=2) + '\n'
    if any(row['Secret'] in serialized for row in findings):
        raise AuditError('Output redaction guard rejected a value in final metadata.')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(serialized, encoding='utf-8')
    print(json.dumps({key: report[key] for key in ('findingCount', 'distinctDetectedValues', 'engineVersion')}))
    return 1 if findings else 0


def cli(argv=None):
    try:
        return main(argv)
    except Exception as error:
        # Exception text and scanner diagnostics can contain untrusted values.
        print(f'Historical audit stopped safely ({type(error).__name__}); raw evidence was not printed.', file=sys.stderr)
        return 2


if __name__ == '__main__':
    sys.exit(cli())
