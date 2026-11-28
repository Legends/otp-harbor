"""Reverify a retained signed repository when a fresh F-Droid refresh fails."""
import argparse
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path, PurePosixPath
import stat
import subprocess
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location('fdroid_repository', Path(__file__).with_name('Build-FDroidRepository.py'))
repository = importlib.util.module_from_spec(spec)
spec.loader.exec_module(repository)


def api(path, binary=False):
    result = subprocess.run(['gh', 'api', 'repos/Legends/otp-harbor/' + path], capture_output=True)
    if result.returncode:
        raise RuntimeError('Could not retrieve trusted last-known-good F-Droid evidence.')
    return result.stdout if binary else json.loads(result.stdout)


def extract_public(data, destination):
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        if sum(item.file_size for item in archive.infolist()) > 1024 * 1024 * 1024:
            raise ValueError('Retained F-Droid repository exceeds the bounded size.')
        for item in archive.infolist():
            name = item.orig_filename
            parts = PurePosixPath(name).parts
            target = destination.joinpath(*parts)
            if ('\\' in name or ':' in name or '\x00' in name or name.startswith('/') or '..' in parts
                    or stat.S_ISLNK(item.external_attr >> 16) or not target.resolve().is_relative_to(destination.resolve())):
                raise ValueError('Unsafe retained repository archive path.')
        archive.extractall(destination)
    repository.check_public_files(destination)


def verify(repo, settings):
    # Reuse F-Droid's pinned cryptographic verifier and the same production pins
    # as fresh repository generation. No signing key is needed to verify.
    from fdroidserver import common, index
    original_directory = Path.cwd()
    with tempfile.TemporaryDirectory(prefix='otp-fdroid-verify-') as directory:
        workspace = Path(directory)
        (workspace / 'config.yml').write_text(json.dumps({'repo_url': settings['url'], 'sdk_path': os.environ.get('ANDROID_HOME', '/usr/lib/android-sdk')}), encoding='utf-8')
        try:
            os.chdir(workspace)
            common.read_config()
            entry, _, _ = index.get_index_from_jar(str(repo / 'entry.jar'), settings['indexCertificateSha256'])
            digest = hashlib.sha256((repo / 'index-v2.json').read_bytes()).hexdigest()
            if entry['index']['sha256'] != digest:
                raise ValueError('Retained signed entry/index digest mismatch.')
            v1, _, _ = index.get_index_from_jar(str(repo / 'index-v1.jar'), settings['indexCertificateSha256'], allow_deprecated=True)
            if v1['repo']['address'] != settings['url'] or set(v1['packages']) != {settings['applicationId']}:
                raise ValueError('Retained repository identity or applications differ.')
            packages = v1['packages'][settings['applicationId']]
            if not packages or len(packages) > settings['retainedVersions']:
                raise ValueError('Unexpected retained package set.')
            expected = set()
            for package in packages:
                path = repo / package['apkName']
                if Path(package['apkName']).name != package['apkName'] or not path.is_file():
                    raise ValueError('Unsafe or missing retained APK.')
                if hashlib.sha256(path.read_bytes()).hexdigest() != package['hash']:
                    raise ValueError('Retained APK digest mismatch.')
                signature = repository.command_output(['apksigner', 'verify', '--print-certs', str(path)])
                badging = repository.command_output(['aapt', 'dump', 'badging', str(path)])
                repository.verify_apk_output(signature, badging, {'code': package['versionCode'], 'version': package['versionName']}, settings)
                expected.add(path.name)
            if {p.name for p in repo.glob('*.apk')} != expected:
                raise ValueError('Unexpected APK outside the signed index.')
        finally:
            os.chdir(original_directory)
    repository.check_public_files(repo)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    artifacts = api('actions/artifacts?name=fdroid-repository&per_page=100')['artifacts']
    selected = None
    for artifact in artifacts:
        if artifact['expired'] or str(artifact['workflow_run']['id']) == os.environ['GITHUB_RUN_ID']:
            continue
        run = api(f"actions/runs/{artifact['workflow_run']['id']}")
        if run['head_branch'] == 'master' and run['conclusion'] == 'success' and run['path'] in ('.github/workflows/pages.yml', '.github/workflows/fdroid-repository.yml'):
            selected = artifact
            break
    if selected is None:
        raise ValueError('No retained trusted signed F-Droid repository is available; website publication remains blocked.')
    data = api(f"actions/artifacts/{selected['id']}/zip", binary=True)
    if selected.get('digest') != 'sha256:' + hashlib.sha256(data).hexdigest():
        raise ValueError('Retained GitHub artifact digest is missing or differs from the downloaded archive.')
    extract_public(data, output)
    settings = json.loads((ROOT / 'packaging/fdroid/repository.json').read_text(encoding='utf-8'))
    repository.validate_settings(settings)
    verify(output, settings)
    print(f"Retaining last-known-good signed F-Droid repository from run {selected['workflow_run']['id']}; fresh Android indexing still requires recovery.")


if __name__ == '__main__':
    main()
