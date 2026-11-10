# README release information

Every successful stable Desktop (`vX.Y.Z`) or Android (`android-vX.Y.Z`) publication in
`build-and-test.yml` calls the reusable `readme-releases.yml` workflow. This explicit call
is required: publishing with `GITHUB_TOKEN` does not trigger another release-event workflow.
Manually published/edited/deleted releases also trigger a refresh. A daily scheduled run
reconciles changes or missed runs, and **Actions → Refresh README releases → Run workflow**
allows an immediate refresh.

The workflow checks out the default branch, checks Microsoft's public Store catalog,
fetches every page of GitHub release metadata, and updates both independent sources.
`scripts/release/Update-ReadmeReleases.py` changes only the block between
`<!-- BEGIN GENERATED RELEASES -->` and `<!-- END GENERATED RELEASES -->`.
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

Microsoft Store approval is independent of GitHub publication. On each refresh,
`Sync-PublicMicrosoftStoreVersion.py` queries Microsoft's public US retail catalog for
product `9P31KH5L924P`. It accepts only an active, purchasable full SKU containing the
matching `Legends77.OTPHarbor` x64 MSIX and publisher identity. The numeric version must
agree with the package name and use this repository's stable version encoding; trials,
preorders, expired/future availability and RC packages are ignored. The existing
`Set-PublicMicrosoftStoreVersion.ps1` maps confirmed package versions back to display
versions (for example, `3.2.65535.0` becomes `3.0.2`) and synchronizes the README badge,
public Store status, `public-version.json`, and Store listing release heading.

The public catalog is not a guaranteed stable API. A timeout, changed schema, missing
package or older catalog response produces a workflow warning and preserves the last
confirmed Store version. The existing badge remains readable without a live Store
request on each README view. Maintainers can still run the established publication
confirmation workflow/updater when an automatic lookup cannot verify availability.
Publishing a GitHub tag never implies Store approval, and the catalog sync never reads
GitHub versions. Updates for other Store markets may roll out at different times.

The Desktop and Android Shields.io badges use separate `v*` / `android-v*` filters,
`sort=date`, and the default prerelease exclusion. Those two badges update directly
from GitHub independently of this workflow; the workflow maintains the detailed release
table and confirmed Store metadata. All three badges preserve separate channel links.
The `winget install --id 9P31KH5L924P --source msstore` command installs that Store edition.

For a deterministic local refresh, save `gh api --paginate --slurp
repos/Legends/otp-harbor/releases?per_page=100` to a JSON file, then run:

```powershell
python scripts/release/Update-ReadmeReleases.py --releases-json releases.json
python scripts/release/Update-ReadmeReleases.py --releases-json releases.json --check
python -m unittest discover -s scripts/validation -p test_readme_releases.py
python -m unittest discover -s scripts/validation -p test_public_store_version.py
python scripts/release/Sync-PublicMicrosoftStoreVersion.py --check
```
