# README release information

Every successful stable Desktop (`vX.Y.Z`) or Android (`android-vX.Y.Z`) publication in
`build-and-test.yml` calls the reusable `readme-releases.yml` workflow. This explicit call
is required: publishing with `GITHUB_TOKEN` does not trigger another release-event workflow.
Manually published/edited/deleted releases also trigger a refresh. A daily scheduled run
reconciles changes or missed runs, and **Actions → Refresh README releases → Run workflow**
allows an immediate refresh.

The workflow checks out the default branch, fetches every page of GitHub release metadata,
and runs `scripts/release/Update-ReadmeReleases.py`. Only the block between
`<!-- BEGIN GENERATED RELEASES -->` and `<!-- END GENERATED RELEASES -->` is changed.
It records the greatest published stable version of each independent channel, publication
date, actual package/integrity assets, and release-note links. Drafts, prereleases, RCs,
and unrelated tags are excluded. Generated commits are idempotent and serialized; a
concurrent default-branch push is retried against fresh metadata.

The job commits the generated README change directly, without creating a PR. It uses
the repository's existing `GH_PAT` secret for checkout/push and GitHub API reads, falling
back to the built-in token (`contents: write`) when that secret is unavailable. The
release pipeline explicitly forwards only `GH_PAT` to the reusable workflow; signing
secrets are not forwarded. The token identity must be authorized to push these README
maintenance commits to the default branch under the repository's protection rules.
The workflow does not alter those rules. If the identity cannot push, the job fails
explicitly instead of leaving a stale README unnoticed. A failed README refresh is a
failed release-pipeline job even though already-published assets remain available; fix
the token permission or metadata problem and rerun it.

Microsoft Store approval is independent of GitHub publication. The README's Store badge
and public Store version are maintained by `Set-PublicMicrosoftStoreVersion.ps1` from
confirmed Store availability; publishing a GitHub tag must never imply Store approval.
The `winget install --id 9P31KH5L924P --source msstore` command installs that Store edition.

For a deterministic local refresh, save `gh api --paginate --slurp
repos/Legends/otp-harbor/releases?per_page=100` to a JSON file, then run:

```powershell
python scripts/release/Update-ReadmeReleases.py --releases-json releases.json
python scripts/release/Update-ReadmeReleases.py --releases-json releases.json --check
python -m unittest discover -s scripts/validation -p test_readme_releases.py
```
