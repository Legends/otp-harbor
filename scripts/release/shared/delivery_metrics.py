"""Read-only delivery measurements. Missing incident/Store history stays unknown."""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import statistics
import subprocess

from release_plan import stable_tag


def instant(value):
    return datetime.fromisoformat(value.replace('Z', '+00:00'))


def duration(start, end):
    return max(0, (instant(end) - instant(start)).total_seconds())


def distribution(values):
    return {'count': len(values), 'medianSeconds': statistics.median(values) if values else None,
            'minSeconds': min(values) if values else None, 'maxSeconds': max(values) if values else None}


def measure(runs, releases, jobs, incidents=None, store_records=None, changes=None):
    ci = []
    for run in runs:
        if run['path'] != '.github/workflows/build-and-test.yml' or run['status'] != 'completed':
            continue
        actual = [job for job in jobs.get(str(run['id']), []) if job.get('started_at') and job.get('completed_at') and job.get('conclusion') != 'skipped']
        start = min((job['started_at'] for job in actual), default=None)
        finish = max((job['completed_at'] for job in actual), default=None)
        feedback = min((job['completed_at'] for job in actual), default=None)
        failed = min((job['completed_at'] for job in actual if job.get('conclusion') == 'failure'), default=None)
        ci.append({'runId': run['id'], 'event': run['event'], 'result': run['conclusion'],
                   'queueSeconds': duration(run['created_at'], start) if start else None,
                   'executionSeconds': duration(start, finish) if start and finish else None,
                   'firstCompletedGateSeconds': duration(run['created_at'], feedback) if feedback else None,
                   'firstFailedGateSeconds': duration(run['created_at'], failed) if failed else None})
    channels = {}
    for family in ('desktop', 'android'):
        selected = []
        for release in releases:
            if release['draft'] or release['prerelease'] or not release.get('published_at'):
                continue
            try:
                tag_family, _ = stable_tag(release['tag_name'])
            except ValueError:
                continue
            if tag_family == family:
                selected.append(release)
        dates = sorted(release['published_at'] for release in selected)
        span = duration(dates[0], dates[-1]) / 86400 if len(dates) > 1 else None
        channels[family] = {'publishedCount': len(dates), 'observedFirstPublication': dates[0] if dates else None,
                            'observedLastPublication': dates[-1] if dates else None,
                            'observedPublicationsPerDay': (len(dates) - 1) / span if span else None,
                            'changeLeadTimeSeconds': None, 'changeFailureRate': None,
                            'failedDeploymentRecoverySeconds': None, 'deploymentReworkRate': None}
        lead_times = [duration(item['committedAt'], item['publishedAt']) for item in changes or [] if item['family'] == family]
        channels[family]['changeLeadTimeSeconds'] = statistics.median(lead_times) if lead_times else None
        channels[family]['changeLeadTimeDistribution'] = distribution(lead_times)
        classified = [item for item in incidents or [] if item.get('family') == family]
        if classified:
            # Operator-supplied classifications must name real attempted deployments.
            if any(not item.get('deploymentId') or not isinstance(item.get('failed'), bool) or not isinstance(item.get('rework'), bool) for item in classified):
                raise ValueError('Incomplete deployment incident classifications.')
            channels[family]['changeFailureRate'] = sum(item['failed'] for item in classified) / len(classified)
            channels[family]['deploymentReworkRate'] = sum(item['rework'] for item in classified) / len(classified)
            recoveries = [duration(item['failedAt'], item['recoveredAt']) for item in classified if item['failed'] and item.get('failedAt') and item.get('recoveredAt')]
            channels[family]['failedDeploymentRecoverySeconds'] = distribution(recoveries)
    store_times = [duration(item['submittedAt'], item['publishedAt']) for item in store_records or [] if item.get('submittedAt') and item.get('publishedAt')]
    phases = {'packageGeneration': [], 'desktopSigningAndMetadata': [], 'androidBuildAndSigning': [], 'storeSubmission': [], 'releasePreparation': []}
    phase_steps = {'Assemble release artifact': 'packageGeneration', 'Build unsigned Partner Center MSIX': 'packageGeneration',
                   'Prepare and verify desktop metadata': 'desktopSigningAndMetadata', 'Generate and verify signed Avalonia feed': 'desktopSigningAndMetadata',
                   'Build and verify signed Android APK': 'androidBuildAndSigning',
                   'Operate on the exact approved MSIX without replacing pending submissions': 'storeSubmission'}
    for run in runs:
        for job in jobs.get(str(run['id']), []):
            if run['path'] == '.github/workflows/release.yml' and job['name'] == 'candidate' and job.get('conclusion') == 'success':
                phases['releasePreparation'].append(duration(run['created_at'], job['completed_at']))
            for step in job.get('steps', []):
                if step['name'] in phase_steps and step.get('conclusion') == 'success' and step.get('started_at') and step.get('completed_at'):
                    phases[phase_steps[step['name']]].append(duration(step['started_at'], step['completed_at']))
    return {'schemaVersion': 1, 'ci': {'runs': ci, 'completedCount': len(ci),
            'workflowFailureFraction': sum(item['result'] == 'failure' for item in ci) / len(ci) if ci else None,
            'execution': distribution([item['executionSeconds'] for item in ci if item['executionSeconds'] is not None])},
            'channels': channels, 'store': {'submissionToPublicationObserved': distribution(store_times)},
            'releasePhases': {key: distribution(value) for key, value in phases.items()},
            'limitations': ['Workflow failures are not DORA deployment failures.',
                           'First completed/failed gate is an observed feedback proxy, not developer acknowledgement.',
                           'Change lead time uses commit committer timestamps in recent ancestry-confirmed published release pairs; incomplete history remains null.',
                           'Store timing is first observed publication, bounded by the reconciliation interval.',
                           'Signing durations include the named build/metadata verification step; cryptographic signing alone is not inferred.',
                           'Change failure/rework/recovery need explicit deployment incident classifications; publication retry frequency needs retained channel status history.',
                           'Metrics describe engineering feedback, not individual productivity.']}


def api(path, paginate=True):
    result = subprocess.run(['gh', 'api', *(['--paginate', '--slurp'] if paginate else []), 'repos/Legends/otp-harbor/' + path], capture_output=True, text=True, encoding='utf-8')
    if result.returncode:
        raise RuntimeError('Delivery history is inaccessible; no metrics fabricated.')
    return json.loads(result.stdout)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--history', type=Path)
    parser.add_argument('--incidents', type=Path)
    parser.add_argument('--store-records', type=Path)
    args = parser.parse_args()
    if args.history:
        history = json.loads(args.history.read_text(encoding='utf-8'))
    else:
        runs = api('actions/runs?per_page=30', paginate=False)['workflow_runs']
        # A bounded recent sample; full release history is paginated independently.
        releases = [item for page in api('releases?per_page=100') for item in page]
        jobs = {}
        for run in runs:
            if run['path'] in ('.github/workflows/build-and-test.yml', '.github/workflows/release.yml', '.github/workflows/release-recovery.yml') and run['status'] == 'completed':
                jobs[str(run['id'])] = [item for page in api(f"actions/runs/{run['id']}/jobs?per_page=100") for item in page['jobs']]
        changes = []
        for family in ('desktop', 'android'):
            selected = []
            for release in releases:
                if release['draft'] or release['prerelease'] or not release.get('published_at'):
                    continue
                try:
                    tag_family, number = stable_tag(release['tag_name'])
                except ValueError:
                    continue
                if tag_family == family:
                    selected.append((number, release))
            selected.sort(key=lambda item: item[0])
            recent = selected[-6:]
            for (_, previous), (_, current) in zip(recent, recent[1:]):
                comparison = api(f"compare/{previous['tag_name']}...{current['tag_name']}?per_page=100")
                commits = {commit['sha']: commit for page in comparison for commit in page['commits']}
                if comparison[0]['status'] not in ('ahead', 'identical') or len(commits) != comparison[0]['total_commits']:
                    continue
                for commit in commits.values():
                    committed_at = commit['commit']['committer']['date']
                    if instant(committed_at) <= instant(current['published_at']):
                        changes.append(dict(family=family, commit=commit['sha'], committedAt=committed_at,
                                            publishedAt=current['published_at'], releaseTag=current['tag_name']))
        history = {'runs': runs, 'releases': releases, 'jobs': jobs, 'changes': changes}
    report = measure(history['runs'], history['releases'], history['jobs'],
                     json.loads(args.incidents.read_text(encoding='utf-8')) if args.incidents else None,
                     json.loads(args.store_records.read_text(encoding='utf-8')) if args.store_records else None, history.get('changes'))
    report['observedAt'] = datetime.now(timezone.utc).isoformat()
    args.output.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(f"Measured {report['ci']['completedCount']} completed CI runs; unavailable delivery classifications remain null.")


if __name__ == '__main__':
    main()
