# Continuous delivery operations

`release.yml` is the primary on-demand interface. It extends the existing packaging,
signing, update-feed and distribution scripts. It does not replace the native application
architecture or create another release service. Read the platform details in
[Microsoft Store](MICROSOFT_STORE.md), [Android](ANDROID.md), and
[public release discovery](README_RELEASES.md).

Production publication and administrative configuration require separate authorization.
The implementation and local/mocked verification do not establish live Store integration.

## Operating model

Keep protected `master` releasable through small, reviewed changes on short-lived branches.
Code, security, packaging, release operations and recovery are shared responsibilities.
Investigate failures without blame and use evidence to improve the whole delivery process.
Keep `release/1.x` unchanged until its maintenance owner confirms it is obsolete.

Continuous delivery is the default: prepare a candidate on demand and approve its
production promotion. Automatic promotion is disabled until repository policy and
dedicated environments explicitly authorize it. Successful CI does not publish a release.

## Workflow responsibilities

| File | Responsibility |
|---|---|
| `build-and-test.yml` | Existing PR/push entry point, required `build-test` check, protected legacy tags and dormant SignPath rehearsal |
| `.github/actions/validate-application/action.yml` | Shared compilation, policy/localization/vulnerability checks, filtered PR or full tests, actual TRX evidence |
| `reusable-validation.yml` | Reusable full validation, Android NativeAOT, Unix security, three-platform UI/native/package probes, explicit aggregate gate |
| `security-audit.yml` | Existing actionlint, dependency review, CodeQL capability detection, secret and dependency scans; reusable for releases |
| `release.yml` | Resolve one source, independent versions, selected preparation, candidate inventory/attestation and independent channel promotion |
| `reusable-desktop-package.yml` | Existing version-specific Windows/Linux preparation and Store MSIX from the validated Windows payload |
| `reusable-android-package.yml` | Existing permanent identity, NativeAOT universal APK and pinned production certificate; protected signing credentials |
| `reusable-release-metadata.yml` | Existing manifest/checksum/appcast generation and signature checks; separate unsigned validation path |
| `reusable-promote.yml` | Protected promotion, original candidate/provenance/signature verification, immutable GitHub assets |
| `reusable-store-submission.yml` | Protected Store submission or bounded read-only certification reconciliation |
| `release-recovery.yml` | Targeted recovery using original run/artifact/digest; no build or version allocation |
| `release-reconciliation.yml` | Scheduled/manual bounded checks of retained Store submissions |
| `readme-releases.yml`, `pages.yml`, `fdroid-repository.yml` | Existing independently confirmed public metadata, website and signed project F-Droid repository |
| `delivery-metrics.yml` | Read-only GitHub delivery measurements, with unavailable classifications explicitly unknown |

The `build-test` and existing security required-check names remain intact. Platform checks
now appear under the reusable `platform-validation` job; inspect any additional custom
branch rules before merging. The existing required-check configuration was read on
2026-11-28; no live protection settings were changed.

## Release commands

Run these after the reviewed workflow is present on `master` and the production
prerequisites below are configured. They are operational commands, not evidence that
publication has already been tested.

```bash
# Windows Store input and Linux release; submit MSIX after authorization.
gh workflow run release.yml --ref master \
  -f scope=desktop -f desktop_version=auto -f mode=approval -f store_action=submit

# Android only, including subsequent public metadata/F-Droid reconciliation.
gh workflow run release.yml --ref master \
  -f scope=mobile -f android_version=auto -f mode=approval

# Both independent versions from the same immutable source.
gh workflow run release.yml --ref master \
  -f scope=all -f desktop_version=auto -f android_version=auto \
  -f mode=approval -f store_action=submit

# No production credentials, tags, releases, Store submission, or repository reservation.
gh workflow run release.yml --ref master -f scope=all -f mode=validate -f store_action=prepare
```

Supply explicit stable versions for a deliberate minor/major release. Canonical `X.Y.Z`
is required; leading zeros, suffixes and extra components are rejected. Desktop and
Android resolve numerically and independently, using occupied tags, published/draft
releases and retained non-validation plan reservations. Desktop planning additionally
reads the public Store catalog through the existing identity/version verifier. Store
submission rechecks the authoritative published and pending application state before
mutation. An unrelated pending submission blocks submission rather than being replaced.

The existing Store mapping and Android versionCode algorithms are unchanged. Their
limits still apply; a patch increment at the supported component limit requires an
explicit supported minor/major version. Historical RC/four-part tags remain immutable;
new production releases are stable only. Android continues to use `make_latest=false`.

Validation runs use unsigned desktop metadata and the existing test-signed Android
NativeAOT probe identity. They record intended versions but are explicitly ineligible
for promotion. Shared Android validation can still run for desktop scope; an unselected
platform's production package is never prepared.

Unsigned validation packaging may continue after a security scan fails so that
independent packaging diagnostics are available. Candidate creation still fails on
any required security gate, in every mode. Such a run does not authorize promotion.
The full-history secret scan uses Gitleaks 8.30.1, which supports the existing global
allowlist syntax. Its historical fixture exception requires the reviewed commit,
exact file path and detector rule together; a regression test runs the real engine.
Unresolved historical findings remain release blockers and require maintainer review.

## Candidate and authorization boundary

Promotion and Store jobs extract candidate data into `release-input`, outside the
trusted scripts in the checkout. Store recovery state is extracted separately and
validated before being copied into that directory. Archives containing unexpected
top-level entries are rejected. Downloaded data cannot replace the verifier which
will later run with publication or Store credentials.

The source is the dispatch's immutable `github.sha`; operators cannot choose another
source through a workflow input. All selected platforms use it. Shared validation runs
once, and an explicit gate rejects failed, cancelled, missing or unexpectedly skipped
required jobs. Only unrelated preparation jobs may be intentionally skipped.

Production requires `master`. Before merging the new dispatch workflow, the existing
manual CI entry can run the same orchestrator on a review branch with mode hardcoded to
validation and no production secrets forwarded:

```bash
gh workflow run build-and-test.yml --ref codex/continuous-delivery-evolution \
  -f candidate_validation_scope=all
```

This optional pre-merge check prepares non-promotable artifacts. Ordinary PR/push CI and
the default manual CI interface retain their existing behavior.

Every production candidate records its plan, source, versions, run/attempt, source
artifact IDs, filenames, byte lengths, SHA-256 hashes, package identities, architectures,
signing results, toolchain pins and actual full-test counters. Existing signed
`release-artifacts-v2.json` and appcast schemas/URLs are preserved. GitHub provenance
attests `candidate.json`, which binds every retained payload file. Promotion verifies
the digest, trusted original workflow, protected-branch ancestry, attestation, inventory,
and applicable final signatures again.

Review the candidate summary and artifact before approving environment deployments.
Desktop and Android promotion jobs can be approved together only after all selected
mandatory preparation has succeeded. Approval authorizes those exact artifacts. A
changed candidate requires new preparation and approval; publication never rebuilds it.
Windows submission inputs remain unsigned MSIX files for Microsoft to sign after
acceptance. Their hashes trace the submission input, not Microsoft's distributed binary.

Candidate/MSIX/production APK evidence is retained for 90 days. Validation-only and
ordinary UI/native probe evidence uses shorter retention. Repository policy must permit
the requested retention. Start recovery before original evidence expires. No expired or
corrupted candidate may be silently reconstructed under its previous approval.

Android's existing signing environment review remains separate from final candidate
promotion: the first authorizes use of the permanent signing key, the second authorizes
the verified final outputs. This protects distinct assets and may require two reviews.
Do not remove the existing signing review merely to reduce clicks.

## Required administrative setup

No environment, reviewer, tag rule, key, or production policy is created or weakened by
the implementation task. Configure and verify these before enabling production use.

| Environment | Protection / data |
|---|---|
| `release-production` | Required reviewer; custom branch policy permitting `master`; permit protected `v*`/`android-v*` tags only if retaining legacy entry |
| `release-signing` | Trusted `master` branch restriction; existing NetSparkle public/private keys as environment secrets |
| `android-release` | Preserve current reviewer and four signing secrets/public certificate variable; restrict to `master` and protected `android-v*` tags for legacy support |
| `store-release` | Required reviewer, `master` only, Store identifiers and client secret below |
| `store-reconciliation` | `master` only, same dedicated Store application credentials; workflow performs GET/status operations only |
| `release-automatic`, `store-automatic` | Optional, disabled operationally by default; trusted `master` only; configure only after explicitly authorizing unattended promotion |

Both `refs/tags/v*` and `refs/tags/android-v*` need active update/deletion protection.
The production planner checks this and the deployment environments before preparation.
Use no routine protection bypass. GitHub permissions and plan/visibility determine which
review features are available; verify actual environment rules rather than relying on
the environment name. Solo maintainers can retain self-review where permitted by their
policy; the workflow does not claim separation of duties in that configuration.

At baseline, `android-release` required review but allowed unrestricted deployment refs.
The new production/Store environments and Android tag immutability rule were absent.
These are external prerequisites, not completed repository features. Existing NetSparkle
keys were repository secrets; migrate them into the trusted signing environments before
activation. Retain any legacy signing access only for the reviewed tag path, then remove
redundant repository-level copies after hosted equivalence is demonstrated. Do not
rotate identities as part of migration. Dormant SignPath production restrictions remain.

Set the repository variable `RELEASE_AUTOMATIC_PROMOTION_ENABLED=true` only after an
explicit production-policy decision and configuration of the automatic environments.
The input `mode=automatic` alone cannot enable it. The same gates, hashes and attestation
checks apply. With the variable absent, automatic requests fail before preparation.
Disabling it also blocks automatic-mode mutation recovery; read-only Store monitoring
can continue. Existing approved candidates retain their original execution mode.

### Optional short-lived GitHub release App

The built-in token is preferred. GitHub can require **Workflows: write** when a release
targets a candidate whose workflow files differ from the current default branch;
`GITHUB_TOKEN` cannot receive that permission. This can affect a delayed/recovered
candidate. See the [official release API restriction](https://docs.github.com/rest/releases/releases#create-a-release).

For that case, install a dedicated GitHub App on **only `Legends/otp-harbor`** with Contents:
write and Workflows: write. Configure environment variable `RELEASE_APP_ID` and protected
environment secret `RELEASE_APP_PRIVATE_KEY` in the promotion environments. Set the
**repository** variable `RELEASE_APP_BOT_LOGIN` to its exact `<app-slug>[bot]` login; do not
override it with a different environment value. The pinned official App-token action
mints a repository-scoped short-lived installation token after approval and revokes it
after the job. No broadly privileged permanent PAT is added.

App-created tags/releases do emit workflow events. The existing CI entry recognizes
the dedicated configured App identity and suppresses duplicate tag preparation; App
release events likewise defer metadata refresh to the orchestrator's explicit call.
The job verifies the minted App slug against repository trigger policy before mutation.
Do not use this dedicated App for unrelated manual publication. Without the optional
App, permission failures stop safely and can be recovered with the original candidate
after the account owner configures the narrowly scoped integration.

Useful read-only verification commands:

```bash
gh api repos/Legends/otp-harbor/environments
gh api repos/Legends/otp-harbor/branches/master/protection
gh api repos/Legends/otp-harbor/rulesets
gh variable list --repo Legends/otp-harbor
gh secret list --repo Legends/otp-harbor --env android-release
```

## Microsoft Entra and Partner Center

The integration evaluates the Microsoft Store Developer CLI but deliberately uses the
documented **v1.0 MSIX submission API**. The CLI's high-level `msstore publish` can delete
and recreate a pending draft, discarding staged metadata. `--noCommit` is not a dry run.
See [Microsoft's CLI guidance](https://learn.microsoft.com/windows/apps/publish/msstore-dev-cli/commands)
and [submission API](https://learn.microsoft.com/windows/uwp/monetize/manage-app-submissions).
No browser automation or undocumented OIDC integration is used. The selected integration
runtime is pinned CPython 3.13.18 and uses its standard library; no unpinned CLI installation
is involved.

Use a dedicated Entra application associated with the Partner Center account and the
existing product `9P31KH5L924P`. The initial app submission/publication and age ratings must
already exist. Microsoft documents the **Manager** role for this API. Verify actual
product access and constrain it where the account supports that; do not claim a narrower
product-only role without administrative evidence. Manager access may be broader than
this product and deserves an explicit account-owner review.

Configure environment variables `STORE_TENANT_ID` and `STORE_CLIENT_ID`, and environment
secret `STORE_CLIENT_SECRET`, in the Store environments. The product ID is fixed in the
reviewed integration. The selected API does not require a Seller ID; do not invent one
or infer CLI credentials from another API family. Authentication uses Microsoft's
documented client-credential token resource `https://manage.devcenter.microsoft.com`.
See [Microsoft's prerequisites/authentication](https://learn.microsoft.com/windows/uwp/monetize/create-and-manage-submissions-using-windows-store-services).
Rotate expiring client secrets through approved account procedures with overlapping
validity, update the protected environments, test read-only authentication, and then
revoke the old credential. No real credential values belong in commands, reports or artifacts.

Submission verifies the original candidate, MSIX bytes/manifest, identity
`Legends77.OTPHarbor`, publisher `CN=84095A7C-6458-436E-ABF2-DC02311E25F9`, x64 coverage and
the existing four-part mapping. It retrieves current published/pending state, rejects
non-advancing versions and unrelated drafts, preserves listing/pricing/markets/settings
and other architecture packages, then uploads the approved MSIX inside the API's ZIP
transport. Azure verifies the standard transport MD5 and the client checks blob length
and checksum. Candidate integrity remains SHA-256.

The adapter modifies package entries and appends a non-secret candidate fingerprint to
certification notes for ownership/recovery. It never deletes a submission. It verifies
the retained settings and architecture coverage before committing for certification.
Microsoft warns that editing an API-created submission in Partner Center can prevent
subsequent API modification; avoid concurrent manual editing of a managed submission.

Safe reads/uploads use bounded retries with exponential delay/jitter and Retry-After.
An owned editable submission is read again after its package update, so recovery uses
the current expiring upload URI and rejects a concurrent ownership/status change.
Long Retry-After values stop the bounded job for a later retry. Interrupted creation and
commit requests are reconciled, never blindly repeated. An uncertain creation without
provable ownership requires operator investigation; automatic adoption/deletion is not
provided. A commit still uncertain after status reconciliation also requires review.

`store-state` is **mutable distribution state**, separate from the immutable candidate.
Its allowlisted record contains the candidate fingerprint, package hash/version,
submission ID, actual Microsoft status and observed timestamps. Workflow retries restore
that state before operating; the same-run state artifact can be replaced as it advances.
Candidate artifacts and published release assets are never overwritten.

## Certification and public metadata

`release-reconciliation.yml` runs every four hours or manually:

```bash
gh workflow run release-reconciliation.yml --ref master
```

It discovers trusted retained pending records and checks each submission in a bounded
job. `PendingCommit`, `CommitStarted`, processing/certification, certification failure,
awaiting publication, published, failed and unknown remain distinct. Unknown/failed
states require attention. Original artifacts or state unavailable beyond retention need
manual recovery; absence of a record is not proof that no remote draft exists.
Existing manual Store publication settings are preserved. If Microsoft awaits a manual
publication decision, the monitor reports that state rather than changing the policy.

An uploaded or submitted package is not publicly available. Only a `Published` response
containing the expected x64 version is marked published internally. Existing public
Store metadata still uses the independently verified retail catalog through
`Sync-PublicMicrosoftStoreVersion.py`, so pending or not-yet-visible versions are never
advertised. Catalog propagation may lag certification; normal scheduled metadata
reconciliation eventually observes actual availability.

GitHub publication explicitly calls `readme-releases.yml`, which explicitly dispatches
Pages/F-Droid synchronization when a confirmed maintainer-token push is not already
expected to trigger it. This removes the observed duplicate push-plus-dispatch website
build without losing the built-in-token fallback. This handles [GitHub's token trigger semantics](https://docs.github.com/actions/how-tos/writing-workflows/choosing-when-your-workflow-runs/triggering-a-workflow):
normal token-created tag/release events do not start another push/release workflow.
The existing `GH_PAT` fallback remains because protected-branch metadata writes have not
been replaced by an equivalently authorized GitHub App. No broad new PAT is introduced.

The project F-Droid repository retains its own signing key and pins; this does not imply
official F-Droid catalog publication. If a fresh refresh fails, Pages can use a retained
artifact only after verifying trusted run provenance, the GitHub archive digest, pinned
F-Droid index signatures, APK signatures/identities and hashes. It reports last-known-good
retention explicitly. With no valid retained repository, publication stays blocked.

## Recovery and partial publication

There is no atomic cross-channel publication. A combined candidate must finish all
selected preparation gates before promotion starts, then each channel records its own
outcome. Preserve already-published Linux/Android assets if another operation fails.

Find the original run, `release-candidate-<run>-<attempt>` artifact ID and candidate SHA-256
in its preparation summary. Use the **original** values:

```bash
# Retry desktop publication without compiling or allocating another version.
gh workflow run release-recovery.yml --ref master -f target=desktop \
  -f original_run_id=RUN_ID -f candidate_artifact_id=ARTIFACT_ID \
  -f candidate_sha256=ORIGINAL_CANDIDATE_SHA256

# Store-only retry; also identify the latest non-secret Store state artifact and its run.
gh workflow run release-recovery.yml --ref master -f target=store \
  -f original_run_id=RUN_ID -f candidate_artifact_id=ARTIFACT_ID \
  -f candidate_sha256=ORIGINAL_CANDIDATE_SHA256 \
  -f store_state_artifact_id=STATE_ARTIFACT_ID -f store_state_run_id=STATE_RUN_ID

# Android publication retry uses target=android with the same original candidate fields.
# Read-only targeted Store status uses target=store-status with the Store state fields.
# A failed README/website/F-Droid refresh can be retried without republishing APKs:
gh workflow run readme-releases.yml --ref master
gh workflow run pages.yml --ref master
gh workflow run fdroid-repository.yml --ref master
```

The publisher verifies existing tags and asset bytes, fills missing draft assets only,
and treats an identical already-published release as read-only success. Conflicting tags,
candidate identities, notes, assets or hashes fail. An older desktop recovery cannot
replace a newer latest release. Store version ordering prevents downgrade publication.

A certification rejection is not automatically resubmitted. Inspect Microsoft's report.
An owned submission which Microsoft makes editable (`PendingCommit`) can resume with
the original candidate. Other rejected, missing or uncertain submissions require an
operator investigation; the adapter will not delete or replace them. A changed package
requires a new verified candidate and authorization. Preserve any successful Linux
release while resolving a Store-only failure.

The release/recovery concurrency group never cancels active production work. GitHub
retains only one pending run per group, not an unlimited FIFO; resubmit superseded pending
requests. Immutable plan reservations avoid reusing recently prepared versions. Store
operations serialize by product. No shared mutable checkout is release state.

Legacy tag entry remains during migration and uses the same extracted packagers and
metadata generators. It now requires verified stable tags, protected master ancestry,
tag immutability and production review. An existing legacy draft/release is protected
from replacement and requires explicit investigation rather than blind rerun. Configure
protections before merging this migration. Remove the legacy path only after hosted
equivalence is demonstrated; tags created by the new publisher's token do not start it.

Prefer a reviewed forward-fix release. Android/Store monotonic versions usually prevent
publishing an older package as a rollback. Do not delete releases, move tags or rotate
signing identities to simulate rollback.

## Security and compatibility review

The threat changes concern release credential exposure, artifact substitution,
unauthorized publication and destructive pending-submission replacement. Candidate
hashes/provenance, selected mandatory gates, protected promotion, tag/asset conflict
checks, bounded non-destructive API recovery and explicit external state address these
paths. Android credentials now enter only the materialization/signing steps, after
restore/tool installation; temporary files stay outside the checkout and cleanup checks
the resolved target. NetSparkle temporary key files are removed in `finally`.

Only non-secret plans, hashes, package metadata, test evidence and allowlisted channel
status enter retained artifacts. Store tokens/upload URLs and all private keys remain
outside manifests and summaries. Existing application identities, production signing
identities, version mappings, Linux formats, public signed schemas and update URLs are
unchanged. No vault, password, backup-format or application authorization code changes.
Administrative protection/credential migration and live external validation are still
required before claiming operational production security.

## Measurements and future platforms

[The initial observed baseline](evidence/2026-11-28-delivery-baseline.json) separates actual
CI timing/failures, channel publication frequency and ancestry-correlated change lead
time. Workflow failures are not DORA deployment failures. Change failure, recovery and
rework rates require explicit incident classifications; unavailable values remain null.
The metrics script accepts classified incident/Store records for those measurements.
Package/signing step durations and observed Store certification time stay separate from
compilation. No application telemetry or individual productivity scoring is introduced.

```bash
gh workflow run delivery-metrics.yml --ref master
python scripts/release/shared/delivery_metrics.py --output delivery-metrics.json
```

Use the evidence before setting targets. The latest successful baseline CI run took
about 12 minutes with Android on the critical path; its main build/test job took 5m55s.
Retain existing content-verified local build reuse; tests and signature/package validation
always execute. Cross-run production recovery uses verified retained artifacts, not a
cached test result.

Native GitHub provenance is implemented for the candidate inventory. GitHub's current
dependency-graph SBOM export does not by itself prove a candidate-specific source/runtime
graph; it is not mislabeled as this candidate's SBOM. Existing locked dependency, license
and vulnerability evidence is retained. A separately reviewed, version-pinned SBOM tool
can be added once its platform/runtime completeness is verified.

macOS/iOS production remains disabled. The centralized scope definition contains their
future groups; activation is a reviewed code/config change, never a dispatch switch.
For macOS, extend the existing desktop matrix/DMG verification with supported architectures,
Developer ID signing and notarization. Preserve current probes/security tests meanwhile.
iOS first needs an app host, compatible macOS/Xcode/.NET workloads, provisioning, signing,
IPA generation, TestFlight and App Store Connect evidence. No speculative Apple signing
or submission workflow is installed.
