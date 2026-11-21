"""Enforce the same image-processing versions locally and in CI."""

from importlib.metadata import PackageNotFoundError, version
from pathlib import Path


def require_pinned_dependencies():
    requirements = Path(__file__).with_name('requirements.txt')
    mismatches = []
    for line in requirements.read_text(encoding='utf-8').splitlines():
        if not line.strip() or line.lstrip().startswith('#'):
            continue
        package, expected = line.strip().split('==', 1)
        try:
            actual = version(package)
        except PackageNotFoundError:
            actual = 'missing'
        if actual != expected:
            mismatches.append(f'{package}: expected {expected}, found {actual}')
    if mismatches:
        raise RuntimeError('Marketing dependencies differ from CI: ' + '; '.join(mismatches)
                           + '. Install scripts/marketing/requirements.txt in the selected Python environment.')


if __name__ == '__main__':
    require_pinned_dependencies()
    print('Marketing dependencies match the pinned CI versions.')
