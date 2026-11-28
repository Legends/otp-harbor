"""Customer-facing notes from declared merged PR changes, using read-only APIs."""
import argparse
import json
from pathlib import Path
import re
import subprocess

from release_plan import stable_tag


def api(path):
    result = subprocess.run(['gh', 'api', '--paginate', '--slurp', 'repos/Legends/otp-harbor/' + path], capture_output=True, text=True, encoding='utf-8')
    if result.returncode:
        raise RuntimeError('Read-only release note generation failed.')
    return json.loads(result.stdout)


def render(tag, source, releases, commits, pulls):
    family, number = stable_tag(tag)
    previous = []
    for item in releases:
        if item['draft'] or item['prerelease']:
            continue
        try:
            item_family, item_number = stable_tag(item['tag_name'])
        except ValueError:
            continue
        if item_family == family and item_number < number:
            previous.append((item_number, item['tag_name']))
    previous_tag = max(previous)[1] if previous else None
    hidden = {'documentation', 'dependencies', 'ci', 'internal', 'skip-changelog'}
    included = [item for item in pulls if item.get('merged_at') and item.get('merge_commit_sha') in commits
                and not hidden.intersection(label['name'].lower() for label in item.get('labels', []))]
    lines = ['## Changes', '']
    for item in sorted(included, key=lambda p: p['number']):
        title = re.sub(r'([\\`*_{}\[\]()<>#!|])', r'\\\1', item['title'].replace('\r', ' ').replace('\n', ' '))
        lines.append(f"- {title} ([#{item['number']}](https://github.com/Legends/otp-harbor/pull/{item['number']}))")
    if not included:
        lines.append('Maintenance release; no user-facing changes were declared in merged pull requests.')
    link = f'https://github.com/Legends/otp-harbor/compare/{previous_tag}...{source}' if previous_tag else f'https://github.com/Legends/otp-harbor/commits/{source}'
    lines += ['', f'[Full changelog]({link})']
    return '\n'.join(lines) + '\n'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--tag', required=True)
    parser.add_argument('--source', required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--validation', action='store_true')
    args = parser.parse_args()
    if not re.fullmatch('[0-9a-f]{40}', args.source):
        raise ValueError('Notes require the immutable source commit.')
    family, number = stable_tag(args.tag)
    if args.validation:
        args.output.write_text(f'Validation candidate for {args.tag} from {args.source}; no production release.\n', encoding='utf-8')
        return
    releases = [item for page in api('releases?per_page=100') for item in page]
    previous = []
    for item in releases:
        try:
            item_family, item_number = stable_tag(item['tag_name'])
        except ValueError:
            continue
        if not item['draft'] and not item['prerelease'] and item_family == family and item_number < number:
            previous.append((item_number, item['tag_name']))
    commits = set()
    if previous:
        comparison = api(f"compare/{max(previous)[1]}...{args.source}?per_page=100")
        commits = {item['sha'] for page in comparison for item in page['commits']}
        if len(commits) != comparison[0]['total_commits']:
            raise ValueError('Incomplete release change history.')
    pulls = [item for page in api('pulls?state=closed&base=master&sort=updated&direction=desc&per_page=100') for item in page]
    args.output.write_text(render(args.tag, args.source, releases, commits, pulls), encoding='utf-8')


if __name__ == '__main__':
    main()
