# Delivery implementation evidence — 2026-11-28

The implementation is reviewable in [draft PR #51](https://github.com/Legends/otp-harbor/pull/51).
It is not activated for production. Historical secret-scan findings and administrative
prerequisites block promotion. No production tag, public release, Store submission,
protected merge, signing-key rotation or protection change was performed.

## Baseline

The initial checkout was `14349a70f16247e8734d59758b75c81016526309`, with a clean working
tree. Remote `master` was newer at `f4361d9ac707c7ff3cad0c2cc2edc59084107396`; the fix
branch was rebased onto that commit. `master`, `release/1.x` and existing maintenance
work remain intact. No unrelated working-tree changes were discarded.

The existing pipeline already supplied platform validation, NativeAOT, packaging,
signing, integrity manifests, update feeds, draft publication, F-Droid and public metadata.
The change extends those paths rather than creating another release service.

Read-only inspection confirmed required `build-test` and security checks, one-reviewer
master protection and immutable desktop tags. Android tags lack equivalent immutability.
The existing `android-release` environment has a reviewer but lacks a ref restriction.
New production/signing/Store environments and Entra configuration are external prerequisites.
Only secret names and public configuration were inspected; no secret values were retrieved.

[Measured baseline](evidence/2026-11-28-delivery-baseline.json): 12 completed CI runs,
median execution 647.5 seconds. One successful run took about 12 minutes, dominated by
Android validation. The sample CI failure fraction is not a deployment change-failure
rate. Incident, rework and Store-certification classifications remain unknown.
Duplicate website push/dispatch executions were observed and the refresh logic adjusted.

## Implementation and trust changes

- Existing required CI checks remain at their entry points. A composite action shares
  application validation; reusable workflows retain Android, Unix, UI and native probes.
- `release.yml` resolves desktop/mobile/all and independent versions against occupied
  tags, releases and retained reservations, then binds every selected target to one SHA.
  It supports validate, approval and explicitly policy-enabled automatic modes.
- Candidate manifests bind package identities, versions, signing results, artifact IDs,
  hashes, source, toolchain and actual test evidence. Production manifest provenance uses
  native GitHub attestations. Promotion/recovery consumes the original artifacts.
- Protected GitHub publication rejects conflicting tags/assets and never clobbers
  published files. Direct reusable calls and explicit metadata refresh handle token
  event suppression. An optional scoped, short-lived GitHub App handles permissions
  unavailable to the normal token; no new permanent broad PAT was added.
- Store submission uses Microsoft's versioned API. It preserves listing/settings and
  other architectures, protects unrelated pending submissions, verifies upload transport,
  reconciles uncertain mutations, and never deletes submissions. Certification monitoring
  is bounded and separate from preparation. Upload is never reported as publication.
- Production and recovery jobs extract downloads outside trusted verification scripts.
  Unexpected archive entries, unsafe paths, changed digests and invalid origin fail.
  Android signing material is isolated to signing steps and removed afterward.
- Existing public discovery, signed update schemas and distribution identities remain.
  A retained F-Droid repository is accepted only after native index/APK signature,
  identity and hash verification. Store public versions still require retail confirmation,
  including the standalone operator-confirmation workflow before it changes metadata.
- External Actions are pinned to full SHAs; Dependabot maintains Actions/NuGet updates.
  Script inputs use environment/argument boundaries. Gitleaks is pinned to an engine
  supporting the existing policy syntax, with a real-engine regression for its exact
  reviewed commit/path/rule exception. Unreviewed findings were not allowlisted.
- Delivery metrics use GitHub data and explicit unknown values. No application telemetry
  or vault, password, backup-format, application authorization or signing-identity changes.

Changed/new files are enumerated in the PR's Files changed view. Principal locations are
`.github/actions/validate-application`, `.github/workflows`, `scripts/release/shared`,
`scripts/release/windows`, the F-Droid recovery helper, focused infrastructure tests,
`AGENTS.md` and existing release documentation. No working release path was removed.
Metadata generation was extracted from the original workflow and reused. Store restore
now precedes build fingerprinting; publish uses `--no-restore`, preserving input integrity.

The icon-pack workflow change concerns Action pinning and safe script inputs only.
No icon assets, imports, attributions or affiliation claims changed. Upstream legal
sources were reviewed: [Aegis licensing/credits](https://github.com/aegis-icons/aegis-icons#readme),
[Simple Icons disclaimer](https://github.com/simple-icons/simple-icons/blob/develop/DISCLAIMER.md),
and [Dashboard Icons license](https://github.com/homarr-labs/dashboard-icons/blob/main/LICENSE).

## Verification

| Evidence | Actual result | Boundary |
|---|---|---|
| Original failing notice-clear test | Replaced fixed-delay assertions with observed completion; 72 focused tests passed | Local application tests |
| Full application test project | 2,815 passed, 6 existing opt-in upstream tests skipped | Final local Release run |
| Actual update verification | 11 passed, including 3 new altered-feed/signature/trusted-key cases | Real existing Ed25519 verifier; ephemeral fixture keys |
| Release infrastructure | 40 passed | Planning, gate, mapping, inventory, publication, recovery, path and test-evidence behavior |
| Partner Center | 16 passed | Mocked API/auth/upload/retry/uncertainty/metadata/architecture/state behavior; no live call |
| Existing README, public Store and F-Droid Python suites | 10 + 9 + 12 passed | Local mocks/fixtures |
| Gitleaks exception regression | 1 passed with actual 8.30.1 engine | Exact reviewed exception only; other path/commit/rule remains detected |
| Local infrastructure total | 88 Python tests passed | Separate from application test counts |
| Repository policy checks | 18 passed | Packaging, update, licensing, localization, build reuse and script layout |
| Workflow lint | actionlint 1.7.12 passed locally | Local shellcheck/pyflakes unavailable; hosted actionlint also passed |
| Real unsigned Store package | MakeAppx succeeded, identity/version/contents validated | Local validation artifact; never uploaded to Partner Center |
| [Hosted PR CI](https://github.com/Legends/otp-harbor/actions/runs/46138338068) | Passed | Existing compilation/tests/platform checks after refactor |
| [Hosted Store package](https://github.com/Legends/otp-harbor/actions/runs/46139757494) | Passed | Corrected restore/fingerprint order |
| [Hosted security checks](https://github.com/Legends/otp-harbor/actions/runs/46139757502) | Passed | PR-diff scanning, actionlint, vulnerability/dependency/CodeQL checks |
| [Full validation-only orchestration](https://github.com/Legends/otp-harbor/actions/runs/46140617530) | Failed safely on historical secret scan | No production operations; detailed results below |

Commands included `dotnet test OtpHarbor.Tests/OtpHarbor.Tests.csproj -c Release`, its
72-test focused filter, `python -m unittest discover -s scripts/validation -p <suite>`,
the 18 policy scripts referenced by the shared validation action, actionlint, and
`git diff --check`. The real MSIX build used `New-MicrosoftStoreMsix.ps1` with the
existing Store identity and validation output below `artifacts`.
The first final targeted test build encountered stale WPF temporary output after the
Store publish (`CS5001` in Updater's generated project). Cleaning only the Updater
project resolved it; the targeted build/tests and subsequent full application run passed.

The hosted orchestration source was `c2537b979ee105f6a9477d1447bc44b4dc3406b2`.
Planning, complete application tests, Android NativeAOT, Unix security, all three UI/native
platform probes, Windows/Linux packages, Store MSIX, unsigned desktop metadata and the
native signed F-Droid recovery diagnostic passed. The candidate gate failed with
`Mandatory release job security did not succeed`; all publication jobs were skipped.
The summary completed. This is evidence of gate enforcement, not a successful release.

Retained TRX files from run `46138261979` contain 3,248 passed, 11 skipped and zero failed:
2,812 application, 423 headless and 13 applicable Unix tests passed. Six upstream icon
tests and five inapplicable Unix tests were skipped. The TRX adapter's summary counter
incorrectly reported zero skips; the evidence writer now counts actual outcomes and
rejects incomplete, unknown, failed or entirely skipped evidence. Regression tests and
reprocessing the retained TRX verified the correction. Latest PR checks remain linked
from the PR; newer recovery/API changes are additionally covered by the local suites.

## Task reconciliation

`DONE` means the stated repository requirement was implemented and checked at the
evidence level shown. `BLOCKED` requires external configuration or an unresolved security
decision. `UNVERIFIED` means implementation exists but the necessary live scenario was
not executed. None of these labels authorize production activation.

| Task | Status | Evidence or remaining work |
|---|---|---|
| T1 — Baseline and original Actions failure | DONE | Read-only baseline; original regression fixed; local and hosted application CI passed |
| T2 — Durable DevOps principles | DONE | Focused AGENTS.md delivery rules; maintenance branches preserved |
| T3 — Existing release contracts | DONE | Existing mappings/identities/schemas retained; policy and behavioral tests; hosted packages |
| T4 — Unified scopes and versions | UNVERIFIED | Planning fixtures and all-scope unsigned hosted preparation passed; production scope executions unauthorized |
| T5 — CI/candidate/publication responsibilities | UNVERIFIED | Reuse passed hosted CI; protected legacy publication and real App/tag propagation remain unexecuted |
| T6 — Feedback, shared validation and aggregate gates | DONE | Baseline collected; shared CI preserved; failed security gate actually blocked candidate/publication |
| T7 — Immutable candidate promotion | UNVERIFIED | Inventory/digest/source tests pass; production attestation and approval/promotion not executed |
| T8 — Authorization modes | BLOCKED | Validate safety checked; missing protected environments/ref/tag policy must be configured by an authorized administrator |
| T9 — Partner Center automation | BLOCKED | API adapter and 16 mocks pass; dedicated Entra association/credentials and live read-only verification unavailable |
| T10 — Discovery/F-Droid/website/Store metadata | UNVERIFIED | Existing contracts/tests and native retained-repository recovery pass; actual publication-driven refresh not performed |
| T11 — Supply-chain hardening | UNVERIFIED | SHA pins, least privilege, safe inputs, isolated artifacts, real scan-policy regression checked; production provenance/credential setup unverified |
| T12 — Serialization/idempotency/recovery | UNVERIFIED | Conflict/resume mocks and concurrency contracts pass; live partial-channel and simultaneous production operations not performed |
| T13 — Visibility and measurement | DONE | Actual baseline, summaries, test evidence and read-only metrics workflow; external classifications remain unknown |
| T14 — Automated infrastructure coverage | DONE | 88 Python tests and 18 policy checks; hosted lint/application/platform/package/native recovery evidence |
| T15 — Operating documentation | DONE | Existing platform guides extended; unified operating guide owns setup, commands and recovery |
| T16 — Final verification and report | DONE | This report reconciles all criteria and distinguishes local, hosted, mocked and unverified operations |
| T17 — Incremental implementation and production restrictions | DONE | Review branch/coherent commits/draft PR; no protected merge, production publication or protection/key changes |
| T18 — Later historical shared-secret removal request | BLOCKED | Sanitized separate mirror verified; published-history replacement conflicts with immutable tags/provenance and was not applied |

## Acceptance criteria

| ID | Requested criterion | Status | Evidence / limitation |
|---|---|---|---|
| C01 | Desktop-only production selection | UNVERIFIED | Desktop plan/gate tests; hosted Windows/Linux/MSIX preparation; no production promotion |
| C02 | Android-only production selection | UNVERIFIED | Mobile plan/gate tests; hosted existing NativeAOT test package; no production signing/publication |
| C03 | Combined selection | UNVERIFIED | Same-source all-scope validation preparation passed; production gate remains blocked |
| C04 | Independent version namespaces | DONE | Numeric auto/explicit/conflict tests; existing desktop and Android tag forms retained |
| C05 | Shared immutable combined source | DONE | Single plan SHA, source checks and candidate identity tests; hosted selected packages bind source |
| C06 | No unselected production packaging | DONE | Scope conditions and planning/gate tests; validation Android probe remains distinct from production |
| C07 | Shared application/security validation retained | DONE | Hosted full tests, native/UI/Unix/security checks; full-history failure blocks promotion |
| C08 | Android NativeAOT retained | DONE | Existing workload/SDK/Java/NDK/compiler settings unchanged; hosted build passed |
| C09 | Android signing identity retained | DONE | Existing application ID/certificate pin/mapping retained; negative certificate tests; no key changes |
| C10 | Windows Store-only policy retained | DONE | Public publisher excludes Windows payload/MSIX; Store policy validators passed |
| C11 | Linux package contracts retained | DONE | Hosted existing deb/tar formats, names, dependency and package probes passed |
| C12 | Signed update-feed contracts retained | DONE | Existing signed schemas/tools/endpoints unchanged; production signing execution remains unverified |
| C13 | Existing F-Droid integration retained | DONE | Signing identity untouched; native retained index/APK verification passed on GitHub |
| C14 | Website/README discovery retained | DONE | Existing discovery/version suites and policy validators passed; actual new publication not performed |
| C15 | Failed/skipped mandatory gates prevent publication | DONE | Failure/skip/cancel tests; hosted security failure blocked candidate and all publication jobs |
| C16 | Candidate integrity metadata | UNVERIFIED | Actual inventory/identity/hash/digest tests pass; approved production candidate not generated |
| C17 | Promote approved artifacts without rebuilding | UNVERIFIED | Recovery/promotion contain no build path; mock identity/idempotency checked; live promotion not performed |
| C18 | Inputs cannot bypass authorization | BLOCKED | Trusted-source/mode/environment preflight and protected jobs implemented; external environment setup missing |
| C19 | Validate cannot publish | DONE | Hosted unsigned run: no production credentials/attestation/signing/tag/release/Store operation; publication jobs skipped |
| C20 | Automatic promotion controlled by policy | DONE | Explicit trusted policy required and default disabled; automatic production execution not enabled |
| C21 | Automated Partner Center upload | UNVERIFIED | Exact MSIX preflight and mocked upload/checksum/resume tests; no live credentials/call |
| C22 | Authorized certification submission | UNVERIFIED | Mocked commit and uncertain-outcome protection; live submission explicitly unauthorized |
| C23 | Protect unrelated Store submissions | DONE | Conflict mocks produce reads only; no delete operation or destructive CLI publication |
| C24 | Independent publication reconciliation | UNVERIFIED | Bounded workflow and real-state fixtures; no live submission exists to reconcile |
| C25 | Public Store metadata only after publication | DONE | Existing retail verifier retained and tested; pending state cannot become public version metadata |
| C26 | Recover partial distribution failures | UNVERIFIED | Original candidate recovery, upload resume and downstream-only paths implemented; live partial publication not performed |
| C27 | Safely reject conflicts/concurrent operations | DONE | Duplicate/version/tag/asset/unknown-ownership tests; serialization without cancellation; live race unverified |
| C28 | Visible status and recovery | DONE | Hosted gate/package summaries, real TRX evidence, channel states and documented original-artifact recovery |
| C29 | Future macOS/iOS architecture | DONE | Existing macOS probes remain green; production eligibility disabled; future requirements documented |
| C30 | Automated release regressions | DONE | 88 local Python tests plus existing policy and application coverage |
| C31 | Existing relevant tests successful | DONE | 2,815 local application passes; hosted application/UI/Unix/platform suites passed; deliberate skips documented |
| C32 | Workflow syntax and validators pass | DONE | Local/hosted actionlint and 18 policy validators; historical security findings remain a separate failure |
| C33 | Documentation matches implementation | DONE | Unified guide plus platform-guide updates, commands, safety/recovery limitations and this report |
| C34 | External blockers explicitly identified | DONE | Prerequisites and unverified live operations below; no production-readiness claim |

## Operational handover and remaining blockers

Use [Continuous delivery operations](CONTINUOUS_DELIVERY.md) for copy-paste desktop,
mobile, combined, validation-only, Store-status and recovery commands, approval procedure,
optional automatic policy, environment variables/secrets and Entra setup. The workflow
must first be reviewed and merged through normal protection; this task does not merge it.

An authorized administrator still needs to configure the documented release/signing/Store
environments, master-only deployment restrictions, required review, Android tag immutability,
environment-scoped NetSparkle credentials and dedicated Partner Center Entra application.
Optional automatic environments/policy and a scoped release App must remain disabled
unless separately approved. Existing GH_PAT functionality was retained pending equivalent
protected-branch metadata authorization. No live Store authentication was attempted.

Full-history scanning reports 46 unresolved generic-key findings after the existing
reviewed load-test exception is correctly applied. Two concern historical
`sharedGithubSecret` examples of unconfirmed origin. No new exception was added for them
or the other findings. The separate sanitized mirror removes their shared value from
eight occurrences in six blobs and verifies zero remaining matches in 19,174 reachable
objects. The snapshot master and review-branch file trees are unchanged by redaction in that copy.
Applying the remote snapshot rewrite would change 1,013 commits, four branches, 126 tags
and 50 pull-request refs. It has not been applied to the working repository or GitHub.
Published histories and protection rules remain intact. A coordinated history migration
also needs provenance/signature and GitHub cached/PR-ref handling, as explained in
[GitHub's removal procedure](https://docs.github.com/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository).

Real GitHub publication, production Android/NetSparkle signing, approval/automatic
promotion, Partner Center upload/certification/publication and production downstream
refresh remain unverified and unauthorized during this implementation. Certification
rejection may require operator investigation or a forward-fix candidate; it is never
silently resubmitted or replaced. No hosted runner waits for external certification.
