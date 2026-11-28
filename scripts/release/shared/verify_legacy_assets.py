"""Read-only final asset verification for the retained protected tag entry path."""
import argparse
import os
from pathlib import Path

from github_release import Conflict, GitHub
from release_candidate import sha256
from release_plan import stable_tag


def verify(github, tag, source, directory):
    family, number = stable_tag(tag)
    expected = ([f'OTP-Harbor-linux-x64-{".".join(map(str, number))}.tar.gz', f'otp-harbor_{".".join(map(str, number))}_amd64.deb',
                 'release-artifacts-v2.json', 'release-artifacts-v2.json.signature', 'appcast-v2.xml', 'appcast-v2.xml.signature']
                if family == 'desktop' else [f'OTP-Harbor-android-universal-{".".join(map(str, number))}.apk',
                                            'android-release.json', 'release-artifacts-v2.json', 'SHA256SUMS'])
    if github.tagsource(tag) != source:
        raise Conflict('Legacy tag no longer identifies the validated source.')
    release = github.api('GET', 'releases/tags/' + tag)
    if not release['draft'] or release['prerelease']:
        raise Conflict('Legacy release must still be the expected stable draft.')
    assets = github.api('GET', f"releases/{release['id']}/assets?per_page=100")
    if len(assets) != len(expected) or {item['name'] for item in assets} != set(expected):
        raise Conflict('Legacy draft asset set differs from the distribution contract.')
    for item in assets:
        path = directory / item['name']
        if not path.is_file() or item['size'] != path.stat().st_size or github.digest(item) != sha256(path):
            raise Conflict('Legacy draft contains a conflicting or corrupted asset.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--tag', required=True)
    parser.add_argument('--source', required=True)
    parser.add_argument('--directory', type=Path, required=True)
    args = parser.parse_args()
    verify(GitHub(os.environ['GITHUB_REPOSITORY']), args.tag, args.source, args.directory)
