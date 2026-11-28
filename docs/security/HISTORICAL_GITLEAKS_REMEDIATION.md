# Historical Gitleaks remediation — 2026-11-28

**GO for the local historical-findings remediation; NO-GO for production promotion.**
All 46 original findings, representing 33 distinct values, are accounted for and
locally remediated. Native Gitleaks **8.30.1** now returns **zero findings / exit 0**
both in the working repository and the original all-remote-ref evidence snapshot
with the reviewed policy. No new hosted validation or production authorization is claimed.

| Stage | Findings | Distinct detected values | Disposition |
| --- | --- | --- | --- |
| Original policy, including existing load-test exception | 46 | 33 | Baseline reproduced on working and all-ref snapshot |
| Original 42 verified fixture exceptions | 4 | 2 | Original exception scopes preserved unchanged |
| Add two authorized IndexNow protocol exceptions | 2 | 1 | Exclusive observed protocol use verified |
| Add two owner-attested synthetic TOTP exceptions | 0 | 0 | Owner confirmation supplies provenance; no generator proof claimed |

The 46 exceptions comprise 42 independently reviewed fixture detections / 31 values,
two owner-attested synthetic TOTP detections / one value and two IndexNow protocol
detections / one value. There are 13 duplicate detections beyond the first occurrence
of each value. These counts do not represent 46 exposed accounts.

The audit results below are the **local pre-integration snapshot**. The owner has
subsequently authorized a normal commit/push to PR #51's existing implementation
branch, PR-description updates and hosted validation-only execution. Hosted results
are tracked in [PR #51](https://github.com/Legends/otp-harbor/pull/51), separately from
this snapshot. History/tag/protection changes, credential rotation, production
deployment and PR merge remain unauthorized. SSH and `GH_TOKEN` configuration and
production signing identities are unchanged. The prepared mirror must not be applied.

## Baseline and scope

- PR #51 source: `f91112a1785ae8025d0752897803d5fe5ed05178`, branch
  `codex/continuous-delivery-evolution`; draft/open/unmerged at review.
- `master`: `f4361d9ac707c7ff3cad0c2cc2edc59084107396`. Other remote heads:
  `fix/android-authentication-policy` at `2a2829bc1273e07e49d7c54174e7f3446b18a6b6`
  and `release/1.x` at `ead1b7676c77d1cb752f15d8f8eecdb46e27985b`.
- Initial worktree was clean for the original audit. This continuation preserved its
  existing local uncommitted patch and unrelated branches.
- Push-disabled SSH evidence mirror: `.codex/tasks/historical-security-baseline-20261128.git`.
  Four branches, 126 tags, 50 PR refs; 1,022 reachable commits and 19,221 objects.
  Working origin remains `git@github.com:Legends/otp-harbor.git`.
- Actual `.gitleaks.toml` is unchanged, SHA-256
  `1520369b727daa79156d3acede5e843bb4bfc932d8f9a8c784a7a6e59d17689d`.
  Its existing load-test exception is separate from these 46 findings.
- Gitleaks native scope is `--full-history --all --diff-filter=tuxdb`, verified from
  [8.30.1 source](https://github.com/gitleaks/gitleaks/blob/v8.30.1/sources/git.go).
  Reachable history and current tracked content are covered; external systems,
  unreachable/deleted refs, other clones and server caches are not proved clean.
- Release inventory: 88 records / 86 published releases / 497 published assets
  (507 including drafts). Asset IDs, sizes and available server digests are compared;
  this is not fresh cryptographic verification of every historical binary.

The complete [46-finding / 33-value inventory](evidence/historical-gitleaks-review-20261128.json)
contains full fingerprints, ref names, containing-commit sets and literal/blob counts.
The [redacted provenance evidence](evidence/historical-provenance-20261128.json)
contains introduction metadata, source context, every matching IndexNow blob/path and
the four historical notification-step variants. Plaintext values, value digests,
author emails, private account details and raw scanner contexts are excluded.
Native scanner reports are captured in memory and never forwarded to logs or files.

## IndexNow: authorized and verified

All **45 matching historical blobs / 66 literal occurrences**, every associated
historical path and current tracked files were reviewed. Observed uses are limited to:

- `site/[REDACTED].txt`: verification file containing exactly the protocol value.
- `.github/workflows/pages.yml`: copying that file and supplying the documented
  IndexNow `key`/`keyLocation` notification payload. All four historical step variants
  use the IndexNow endpoint and no other authentication endpoint.
- `scripts/validation/Test-PublicWebsite.ps1`: checking the verification value/file.
- `scripts/marketing/Publish-LocalPreview.ps1`: copying the same public site asset.
- `docs/release/WEBSITE_INDEXING.md`: documenting its public verification endpoint.

A read-only GET of the intended deployed verification URL returned **HTTP 200**, and
the response body matched the configured value exactly. The value-bearing URL and
body were not logged. The website validator also passes. No IndexNow notification
was submitted, no key was rotated and nothing was redeployed.

The [official protocol](https://www.indexnow.org/documentation) requires a hosted key
file proving host ownership; its [FAQ](https://www.indexnow.org/faq) explicitly requires
public access without login. Public serving is intentional but does not eliminate
notification/quota abuse risk: the documentation also advises limiting knowledge of
the key/location. The owner's explicit protocol-exception authorization is recorded
separately from the technical proof. No other credential reuse was observed in the
repository's reachable history or current tracked files; external reuse cannot be
determined from repository evidence.

Only F001 and F046 were excepted, each binding the **original full commit SHA, exact
file path, `generic-api-key` detector and start line**. There is no global value,
entropy, filename-pattern or broad path exclusion. A new real-engine regression
proves that unrelated credentials in the same files and the same synthetic stand-in
value in another file remain detectable. Future commits, other lines and other
detectors remain covered by the existing real-engine scope regression.

## sharedGithubSecret: exact redacted provenance

The earliest reachable appearance is application documentation, not a test fixture.
It was introduced in a **commented JSON example** inside
`Github2FA.MainWindow.Button_Click(object sender, RoutedEventArgs e)`. Runtime code
reads the configured value from user secrets; the example is not a synthetic generator.
About 14 minutes later the example was copied into the README.

| Event | Commit | Author date | Public author name | Full commit message | Original path |
| --- | --- | --- | --- | --- | --- |
| First appearance | `4a481cc89102ef3608d3b9614afe1092845f7585` | 2024-04-04T22:13:02+02:00 | Admin | `comment added` | `Github2FA/MainWindow.xaml.cs:40` |
| README copy | `60607037c1c1c0207b06d83edb42d1a3deaad2ff` | 2024-04-04T22:26:49+02:00 | Admin | `comments` | `readme.md:15` |

Relevant original context, with all string values and URLs redacted. These excerpts
are source evidence only; **none of this code was executed** and no authentication
codes were computed for the historical value.

`4a481cc89102:Github2FA/MainWindow.xaml.cs`

```text
 31:         private async void Button_Click(object sender, RoutedEventArgs e)
 32:         {
 33:             // the github secret key can originally be obtained from here:
 34:             // [URL redacted]
 35:             // Click => Two-Factor methods: => Authenticator app => ... => Edit
 36:             // Either you scan the QR code now with your phone or you click on the link below to get the secret code/key:
 37:             // "[literal redacted]"
 38:             // Now right-click your project and click "[literal redacted]" and add the key there like:
 39:             // {
 40:             //  "[literal redacted]": "[literal redacted]"
 41:             // }
 42:
 43:             string sharedSecret = _configuration["[literal redacted]"]; //"[literal redacted]";
 44:
 45:             var totp = new Totp(Base32Encoding.ToBytes(sharedSecret));
 46:
 47:             // Generate a TOTP code:
 48:             string totpCode = totp.ComputeTotp(); // This generates a TOTP code for the current time.
```

`60607037c1c1:readme.md`

```text
  4:
  5: The github secret key can originally be obtained from here:
  6:  [URL redacted]
  7:
  8:  > Click => Two-Factor methods: => Authenticator app => ... => Edit
  9:
 10:  Either you scan the QR code now with your phone or you click on the link below to get the secret code/key:
 11:  "[literal redacted]"
 12:  Now right-click your VS project and click "[literal redacted]" and add the key there like:
 13:
 14: 	 {
 15: 	  "[literal redacted]": "[literal redacted]"
 16: 	 }
```


The shared value appears eight times in six distinct historical blobs and 29 complete
commit trees, with zero current remote-ref tip or tracked-HEAD occurrence. No historical
test/demo generator establishing its synthetic origin was found. The later demo-data
generator proves other fixtures, not this 2024 example. The comment's GitHub setup
instructions explain how a user would import a real seed; they do not prove whether
this particular example was generated synthetically or taken from a real account.

The owner subsequently stated: **“in the repo i have only synthetic totp base 32 codes
for sure”**. In the active investigation of these historical examples, this is accepted
as owner attestation that they are synthetic. Evidence E07 explicitly distinguishes
that attestation from independent reproducible generator proof. No prior revocation
is asserted or required for the attested synthetic example. Only F002 and F042 receive
exact immutable-fingerprint exceptions; no Base32/value-wide exclusion is introduced.

The previously unresolved owner question was: **“Did the exact Base32 value in commit
`4a481cc89102ef3608d3b9614afe1092845f7585`, `Github2FA/MainWindow.xaml.cs:40`, and copied
to `60607037c1c1c0207b06d83edb42d1a3deaad2ff`, `readme.md:15`, ever authenticate a real
account, or was it created exclusively as synthetic example data?”** The owner's
confirmation supplies the synthetic disposition. If that statement is corrected or
was intended to exclude historical examples, remove those two exceptions and reopen
the production blocker. No further historical-credential decision is pending on the
stated owner evidence. TOTP rotation remains unauthorized and was not attempted.

## Original 42 fixture exceptions: scope unchanged

Each original entry was compared against the retained pre-continuation inventory.
All 42 full commit/path/detector/line fingerprints are identical. Their evidence is:

| Evidence | Proof and limits |
| --- | --- |
| E01 | Two presentation-test detections match RFC 6238 Appendix B's public SHA-1 test vector |
| E02 | Reviewed historical generator reproduces all 26 demo-account seeds, including all 25 detections; media generator not executed |
| E03 | Six legacy crypto JSON fixtures match their historical versions, three PBKDF2 hashes recomputed from the declared synthetic password, three actual migration tests pass including DEK unwrap |
| E04 | Conflict/TwoFAS fixtures explicitly documented as synthetic by acceptance guides and kit builder; U032 matches a public-vector prefix; U033's purpose is documented, external reuse not tested |
| E05 | IndexNow exclusive observed use, public verification GET and owner protocol authorization |
| E06 | Historical source provenance and current absence of legacy example; source alone does not establish synthetic generation |
| E07 | Explicit owner attestation of synthetic TOTP data, accepted for the historical examples under discussion |

Primary public-vector reference: [RFC 6238 Appendix B](https://www.rfc-editor.org/rfc/rfc6238#appendix-B).
Repository proof sources: `OtpHarbor.Tests/Fixtures/LegacySettings/README.md`,
`LegacySettingsFixtureTests`, `docs/testing/MIXED_ACCOUNT_CONFLICT_SCENARIO.md`,
`docs/testing/V3_RELEASE_PHYSICAL_ACCEPTANCE.md` and the physical acceptance-kit builder.
Historical demo generator: `scripts/marketing/Prepare-DemoData.py` at
`c72ba6270d919ddca700570348e56fb59d075101`, blob `ed5ab5678d053a445df11c8c2a8fe371421aaefb`.

## All 46 findings

Detector is `generic-api-key` for every row. Locations are historical detection
paths/lines. Duplicate means an identical detected value, not another account.
Ref-set IDs link to complete detection-commit and ref metadata below/in JSON.
No row now requires credential rotation or unresolved owner intervention; E07 remains
an owner attestation rather than a mathematical generation proof.

| Finding | Value | Historical file:line | Commit/ref set | Duplicate of | Classification/evidence | Exact local remediation |
| --- | --- | --- | --- | --- | --- | --- |
| F001 | U001 | `.github/workflows/pages.yml:141` | `23837c18eb58` / R01 | First | Authorized public IndexNow protocol key / E05 | Exact authorized protocol fingerprint |
| F002 | U002 | `Github2FA/MainWindow.xaml.cs:40` | `4a481cc89102` / R02 | First | Owner-attested synthetic TOTP example / E06,E07 | Exact owner-attested fixture fingerprint |
| F003 | U003 | `TOTP.Tests/Avalonia/Mobile/MobileShellViewModelTests.cs:22` | `71b968b137df` / R04 | First | Verified test fixture / E01 | Exact fixture fingerprint |
| F004 | U003 | `TOTP.Tests/Avalonia/Presentation/AccountListViewModelTests.cs:22` | `71b968b137df` / R04 | F003 | Verified test fixture / E01 | Exact fixture fingerprint |
| F005 | U004 | `TOTP.Tests/Fixtures/LegacySettings/v0-authorization-profile-pbkdf2.json:3` | `83d65da871ec` / R05 | First | Verified crypto fixture / E03 | Exact fixture fingerprint |
| F006 | U005 | `TOTP.Tests/Fixtures/LegacySettings/v0-authorization-profile-pbkdf2.json:4` | `83d65da871ec` / R05 | First | Verified crypto fixture / E03 | Exact fixture fingerprint |
| F007 | U004 | `TOTP.Tests/Fixtures/LegacySettings/v1-global-profile-minimal-pbkdf2.json:4` | `83d65da871ec` / R05 | F005 | Verified crypto fixture / E03 | Exact fixture fingerprint |
| F008 | U005 | `TOTP.Tests/Fixtures/LegacySettings/v1-global-profile-minimal-pbkdf2.json:5` | `83d65da871ec` / R05 | F006 | Verified crypto fixture / E03 | Exact fixture fingerprint |
| F009 | U004 | `TOTP.Tests/Fixtures/LegacySettings/v2-global-profile-expanded-pbkdf2.json:5` | `83d65da871ec` / R05 | F005 | Verified crypto fixture / E03 | Exact fixture fingerprint |
| F010 | U005 | `TOTP.Tests/Fixtures/LegacySettings/v2-global-profile-expanded-pbkdf2.json:6` | `83d65da871ec` / R05 | F006 | Verified crypto fixture / E03 | Exact fixture fingerprint |
| F011 | U004 | `TOTP.Tests/Fixtures/LegacySettings/v3-global-profile-dek-envelope.json:5` | `83d65da871ec` / R05 | F005 | Verified crypto fixture / E03 | Exact fixture fingerprint |
| F012 | U006 | `TOTP.Tests/Fixtures/LegacySettings/v3-global-profile-dek-envelope.json:8` | `83d65da871ec` / R05 | First | Verified crypto fixture / E03 | Exact fixture fingerprint |
| F013 | U004 | `TOTP.Tests/Fixtures/LegacySettings/v4-app-settings-dek-envelope.json:5` | `83d65da871ec` / R05 | F005 | Verified crypto fixture / E03 | Exact fixture fingerprint |
| F014 | U006 | `TOTP.Tests/Fixtures/LegacySettings/v4-app-settings-dek-envelope.json:8` | `83d65da871ec` / R05 | F012 | Verified crypto fixture / E03 | Exact fixture fingerprint |
| F015 | U004 | `TOTP.Tests/Fixtures/LegacySettings/v5-current-app-settings.json:6` | `83d65da871ec` / R05 | F005 | Verified crypto fixture / E03 | Exact fixture fingerprint |
| F016 | U006 | `TOTP.Tests/Fixtures/LegacySettings/v5-current-app-settings.json:9` | `83d65da871ec` / R05 | F012 | Verified crypto fixture / E03 | Exact fixture fingerprint |
| F017 | U007 | `artifacts/marketing/demo-data/otp-harbor-demo-accounts.json:5` | `c72ba6270d91` / R06 | First | Verified test fixture / E02 | Exact fixture fingerprint |
| F018 | U008 | `artifacts/marketing/demo-data/otp-harbor-demo-accounts.json:13` | `c72ba6270d91` / R06 | First | Verified test fixture / E02 | Exact fixture fingerprint |
| F019 | U009 | `artifacts/marketing/demo-data/otp-harbor-demo-accounts.json:21` | `c72ba6270d91` / R06 | First | Verified test fixture / E02 | Exact fixture fingerprint |
| F020 | U010 | `artifacts/marketing/demo-data/otp-harbor-demo-accounts.json:29` | `c72ba6270d91` / R06 | First | Verified test fixture / E02 | Exact fixture fingerprint |
| F021 | U011 | `artifacts/marketing/demo-data/otp-harbor-demo-accounts.json:37` | `c72ba6270d91` / R06 | First | Verified test fixture / E02 | Exact fixture fingerprint |
| F022 | U012 | `artifacts/marketing/demo-data/otp-harbor-demo-accounts.json:45` | `c72ba6270d91` / R06 | First | Verified test fixture / E02 | Exact fixture fingerprint |
| F023 | U013 | `artifacts/marketing/demo-data/otp-harbor-demo-accounts.json:53` | `c72ba6270d91` / R06 | First | Verified test fixture / E02 | Exact fixture fingerprint |
| F024 | U014 | `artifacts/marketing/demo-data/otp-harbor-demo-accounts.json:61` | `c72ba6270d91` / R06 | First | Verified test fixture / E02 | Exact fixture fingerprint |
| F025 | U015 | `artifacts/marketing/demo-data/otp-harbor-demo-accounts.json:69` | `c72ba6270d91` / R06 | First | Verified test fixture / E02 | Exact fixture fingerprint |
| F026 | U016 | `artifacts/marketing/demo-data/otp-harbor-demo-accounts.json:77` | `c72ba6270d91` / R06 | First | Verified test fixture / E02 | Exact fixture fingerprint |
| F027 | U017 | `artifacts/marketing/demo-data/otp-harbor-demo-accounts.json:85` | `c72ba6270d91` / R06 | First | Verified test fixture / E02 | Exact fixture fingerprint |
| F028 | U018 | `artifacts/marketing/demo-data/otp-harbor-demo-accounts.json:93` | `c72ba6270d91` / R06 | First | Verified test fixture / E02 | Exact fixture fingerprint |
| F029 | U019 | `artifacts/marketing/demo-data/otp-harbor-demo-accounts.json:101` | `c72ba6270d91` / R06 | First | Verified test fixture / E02 | Exact fixture fingerprint |
| F030 | U020 | `artifacts/marketing/demo-data/otp-harbor-demo-accounts.json:109` | `c72ba6270d91` / R06 | First | Verified test fixture / E02 | Exact fixture fingerprint |
| F031 | U021 | `artifacts/marketing/demo-data/otp-harbor-demo-accounts.json:117` | `c72ba6270d91` / R06 | First | Verified test fixture / E02 | Exact fixture fingerprint |
| F032 | U022 | `artifacts/marketing/demo-data/otp-harbor-demo-accounts.json:125` | `c72ba6270d91` / R06 | First | Verified test fixture / E02 | Exact fixture fingerprint |
| F033 | U023 | `artifacts/marketing/demo-data/otp-harbor-demo-accounts.json:133` | `c72ba6270d91` / R06 | First | Verified test fixture / E02 | Exact fixture fingerprint |
| F034 | U024 | `artifacts/marketing/demo-data/otp-harbor-demo-accounts.json:141` | `c72ba6270d91` / R06 | First | Verified test fixture / E02 | Exact fixture fingerprint |
| F035 | U025 | `artifacts/marketing/demo-data/otp-harbor-demo-accounts.json:149` | `c72ba6270d91` / R06 | First | Verified test fixture / E02 | Exact fixture fingerprint |
| F036 | U026 | `artifacts/marketing/demo-data/otp-harbor-demo-accounts.json:157` | `c72ba6270d91` / R06 | First | Verified test fixture / E02 | Exact fixture fingerprint |
| F037 | U027 | `artifacts/marketing/demo-data/otp-harbor-demo-accounts.json:165` | `c72ba6270d91` / R06 | First | Verified test fixture / E02 | Exact fixture fingerprint |
| F038 | U028 | `artifacts/marketing/demo-data/otp-harbor-demo-accounts.json:173` | `c72ba6270d91` / R06 | First | Verified test fixture / E02 | Exact fixture fingerprint |
| F039 | U029 | `artifacts/marketing/demo-data/otp-harbor-demo-accounts.json:181` | `c72ba6270d91` / R06 | First | Verified test fixture / E02 | Exact fixture fingerprint |
| F040 | U030 | `artifacts/marketing/demo-data/otp-harbor-demo-accounts.json:197` | `c72ba6270d91` / R06 | First | Verified test fixture / E02 | Exact fixture fingerprint |
| F041 | U031 | `artifacts/marketing/demo-data/otp-harbor-demo-accounts.json:205` | `c72ba6270d91` / R06 | First | Verified test fixture / E02 | Exact fixture fingerprint |
| F042 | U002 | `readme.md:15` | `60607037c1c1` / R03 | F002 | Owner-attested synthetic TOTP example / E06,E07 | Exact owner-attested fixture fingerprint |
| F043 | U032 | `scripts/testing/fixtures/mixed-account-older.json:13` | `c72ba6270d91` / R06 | First | Verified test fixture / E04 | Exact fixture fingerprint |
| F044 | U033 | `scripts/testing/fixtures/mixed-account-older.json:21` | `c72ba6270d91` / R06 | First | Verified test fixture / E04 | Exact fixture fingerprint |
| F045 | U033 | `scripts/testing/fixtures/release-twofas.2fas:5` | `c72ba6270d91` / R06 | F044 | Verified test fixture / E04 | Exact fixture fingerprint |
| F046 | U001 | `scripts/validation/Test-PublicWebsite.ps1:29` | `23837c18eb58` / R01 | F001 | Authorized public IndexNow protocol key / E05 | Exact authorized protocol fingerprint |

## Detection commits and affected refs

Counts describe history reachability, not value presence at every tip. JSON `refSets`
provides every exact ref; `commitSets` provides every full source tree containing a
literal value, including descendants beyond these six detection commits.

| Ref set | Full detection commit | Branches | Tags | PR refs |
| --- | --- | --- | --- | --- |
| R01 | `23837c18eb58effaf6744cbd47c80dbaac906205` | 3 | 20 | 5 |
| R02 | `4a481cc89102ef3608d3b9614afe1092845f7585` | 4 | 126 | 50 |
| R03 | `60607037c1c1c0207b06d83edb42d1a3deaad2ff` | 4 | 126 | 50 |
| R04 | `71b968b137df4f5fd61729de92bf29410bf9d199` | 3 | 14 | 5 |
| R05 | `83d65da871ec28d99a0eb4d7a02df5f83786625f` | 3 | 48 | 23 |
| R06 | `c72ba6270d919ddca700570348e56fb59d075101` | 3 | 13 | 5 |

## All 33 distinct values

Occurrences count literal matches in distinct reachable blobs. Commit-tree counts
measure complete source trees; tips count current remote-ref trees containing a value.
Neither count equals the number of accounts or diff findings. No matching values were
found in reachable commit/tag messages. JSON provides exact membership and blob IDs.

| Value | Findings | Literal occurrences / blobs | Commit trees / tips | Evidence | Disposition |
| --- | --- | --- | --- | --- | --- |
| U001 | 2 | 66 / 45 | 233 / 28 | E05 | Exact authorized protocol fingerprint |
| U002 | 2 | 8 / 6 | 29 / 0 | E06,E07 | Exact owner-attested fixture fingerprint |
| U003 | 2 | 62 / 58 | 273 / 54 | E01 | Exact fixture fingerprint |
| U004 | 6 | 12 / 8 | 452 / 74 | E03 | Exact fixture fingerprint |
| U005 | 3 | 3 / 3 | 452 / 74 | E03 | Exact fixture fingerprint |
| U006 | 3 | 3 / 3 | 452 / 74 | E03 | Exact fixture fingerprint |
| U007 | 1 | 1 / 1 | 88 / 21 | E02 | Exact fixture fingerprint |
| U008 | 1 | 1 / 1 | 88 / 21 | E02 | Exact fixture fingerprint |
| U009 | 1 | 1 / 1 | 88 / 21 | E02 | Exact fixture fingerprint |
| U010 | 1 | 1 / 1 | 88 / 21 | E02 | Exact fixture fingerprint |
| U011 | 1 | 1 / 1 | 88 / 21 | E02 | Exact fixture fingerprint |
| U012 | 1 | 1 / 1 | 88 / 21 | E02 | Exact fixture fingerprint |
| U013 | 1 | 1 / 1 | 88 / 21 | E02 | Exact fixture fingerprint |
| U014 | 1 | 1 / 1 | 88 / 21 | E02 | Exact fixture fingerprint |
| U015 | 1 | 1 / 1 | 88 / 21 | E02 | Exact fixture fingerprint |
| U016 | 1 | 1 / 1 | 88 / 21 | E02 | Exact fixture fingerprint |
| U017 | 1 | 1 / 1 | 88 / 21 | E02 | Exact fixture fingerprint |
| U018 | 1 | 1 / 1 | 88 / 21 | E02 | Exact fixture fingerprint |
| U019 | 1 | 1 / 1 | 88 / 21 | E02 | Exact fixture fingerprint |
| U020 | 1 | 1 / 1 | 88 / 21 | E02 | Exact fixture fingerprint |
| U021 | 1 | 1 / 1 | 88 / 21 | E02 | Exact fixture fingerprint |
| U022 | 1 | 1 / 1 | 88 / 21 | E02 | Exact fixture fingerprint |
| U023 | 1 | 1 / 1 | 88 / 21 | E02 | Exact fixture fingerprint |
| U024 | 1 | 1 / 1 | 88 / 21 | E02 | Exact fixture fingerprint |
| U025 | 1 | 1 / 1 | 88 / 21 | E02 | Exact fixture fingerprint |
| U026 | 1 | 1 / 1 | 88 / 21 | E02 | Exact fixture fingerprint |
| U027 | 1 | 1 / 1 | 88 / 21 | E02 | Exact fixture fingerprint |
| U028 | 1 | 1 / 1 | 88 / 21 | E02 | Exact fixture fingerprint |
| U029 | 1 | 1 / 1 | 88 / 21 | E02 | Exact fixture fingerprint |
| U030 | 1 | 1 / 1 | 88 / 21 | E02 | Exact fixture fingerprint |
| U031 | 1 | 1 / 1 | 88 / 21 | E02 | Exact fixture fingerprint |
| U032 | 1 | 132 / 65 | 273 / 54 | E04 | Exact fixture fingerprint |
| U033 | 2 | 2 / 2 | 88 / 21 | E04 | Exact fixture fingerprint |

## History rewrite remains rejected

The separate `.codex/tasks/shared-github-secret-cleanup-review.git` remains push-disabled
and unapplied. It is three PR #51 commits behind, rewrites 1,013 of 1,019 snapshot
commits, and changes four branches, 126 tags and 50 PR refs. It removes only F002/F042.
With actual unchanged policy it produces 537 findings because 493 previously excepted
load-test hits lose their original commit binding; diagnostic remapping of only that
existing exception leaves 44 original findings. Neither result is a clean scan.

Thirty-five original signed commit objects were inspected locally. The old PR merge
`d91f78e80de8c203b7e5022b9d50f45eb2d784f7`, absent locally, remains retrievable by SHA
through read-only GitHub API with a present verified signature, accounting for 36
original signed objects. None retains its signature in the rewritten mirror. No signed
tags were detected. This is signature-presence evidence, not fresh local verification
of every signature.

The stale head snapshot is `3e5ac298aea120162344ec6871559f9ab1b6eef9`; current PR head is
`f91112a1785ae8025d0752897803d5fe5ed05178`. Its old merge differs from current
`f9991046045a33b443c6155f86e5992d55da4cd2`. Applying it risks dropping three fixes.
Retargeting published tags changes source archives and commit-linked provenance;
uploaded release asset bytes/signatures are not automatically rewritten, which does
not make changed source provenance acceptable. No rewrite is needed for the authorized
protocol material or owner-attested synthetic examples.
[GitHub's sensitive-data removal guidance](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository)
also explains why revocation comes first for real credentials and why other clones,
cached objects and PR refs survive ordinary ref rewriting.

## Validation and changed files

| Check | Actual local result |
| --- | --- |
| Native Gitleaks 8.30.1, working full history | 0 findings / exit 0 |
| Native Gitleaks 8.30.1, original all-remote-ref snapshot with reviewed policy | 0 findings / exit 0 |
| Original policy baseline | 46 findings / 33 values; exact baseline retained |
| Policy/privacy regression tests | 9 passed, 0 skipped; three exercise the real pinned engine |
| Release planning/candidate/publication/contracts | 40 passed, 0 skipped |
| Actual LegacySettingsFixtureTests | 3 passed, 0 skipped; existing unchanged build reused |
| Website, Android packaging, unsigned update policy, Store artifact, script-layout and build-reuse validators | All six passed |
| Actionlint 1.7.12 | Passed; optional ShellCheck/Pyflakes integrations unavailable and disabled locally |
| Patch/report privacy and native changed-content scan | All 11 changed/new files checked against all 33 baseline values; zero matches and zero native findings |
| Ref/asset preservation and whitespace | All 180 refs and metadata for 497 published assets unchanged; whitespace checks pass |
| Hosted workflow at audit snapshot | Not yet run; subsequent integration evidence is recorded in PR #51 |

The root audit/helper still preserves finding exit 1 and execution/privacy failure
exit 2; zero is returned only for a genuinely empty native report. Fingerprint ignores
are version-sensitive; keep 8.30.1 pinned and run real-engine scope tests before upgrades.
[Pinned Gitleaks documentation](https://github.com/gitleaks/gitleaks/blob/v8.30.1/README.md).

Modified/new files across the retained patch and this continuation:

- `.gitleaksignore` — original 42 fingerprints unchanged; four specifically reviewed additions.
- `scripts/validation/Audit-HistoricalGitleaks.py` and `test_historical_gitleaks.py` —
  retained value-free audit and privacy/error-status regression coverage.
- `scripts/validation/test_gitleaks_policy.py` — updated evidence consistency and
  protocol-specific unrelated-credential regression, retaining original scope tests.
- `.github/workflows/security-audit.yml` — retained discovery of both Gitleaks test modules.
- This report, `docs/security/evidence/historical-gitleaks-review-20261128.json` and
  `docs/security/evidence/historical-provenance-20261128.json` — redacted evidence.
- `docs/README.md`, `docs/release/CONTINUOUS_DELIVERY.md` and
  `docs/release/DELIVERY_IMPLEMENTATION_REPORT.md` — current outcome and report links.

The task ledger/scratch mirrors/scripts remain ignored, outside the proposed commit.
No application code, signing configuration, verification endpoint or published asset changed.

## Repeatable verification and recovery

Run from the repository root; use the official 8.30.1 engine. The path below is this
session's verified cache; adapt the installation directory on another machine.

```powershell
$env:GITLEAKS_BINARY = Join-Path $env:TEMP 'otp-harbor-delivery-tools/gitleaks-8.30.1/gitleaks.exe'
python -m unittest discover -s scripts/validation -p 'test_*gitleaks*.py'
python scripts/validation/Audit-HistoricalGitleaks.py --output .codex/tasks/historical-verification.json
python scripts/validation/Audit-HistoricalGitleaks.py --repository .codex/tasks/historical-security-baseline-20261128.git --ignore-path .gitleaksignore --output .codex/tasks/historical-all-refs-verification.json
python -m unittest discover -s scripts/validation -p 'test_release_*.py'
dotnet test OtpHarbor.Tests/OtpHarbor.Tests.csproj -c Release --no-build --filter FullyQualifiedName~LegacySettingsFixtureTests --nologo --verbosity minimal
```

To reproduce the original 46 without altering the current policy, scan the untouched
bare snapshot with an explicitly empty ignore file:

```powershell
Set-Content -LiteralPath .codex/tasks/empty-audit-ignore -Value '' -NoNewline
python scripts/validation/Audit-HistoricalGitleaks.py --repository .codex/tasks/historical-security-baseline-20261128.git --ignore-path .codex/tasks/empty-audit-ignore --output .codex/tasks/historical-baseline-reproduced.json
```

Never print raw scanner reports or pass credential values on the command line. If
the owner attestation is withdrawn, remove only F002/F042 and rerun: two findings / one
value must return with exit 1. If IndexNow scope/authorization is withdrawn, remove
only F001/F046; do not substitute a value-wide exclusion. Removing all 46 new entries
restores the original baseline. Preserve unrelated work; no blanket reset/clean,
tag replacement or mirror push is part of recovery.

## Task reconciliation and remaining decisions

| Task | Status | Evidence |
| --- | --- | --- |
| T1–T6 — Original audit/remediation | DONE | Full baseline, 46/33 inventory, strategy/mirror assessment, local patch and tests retained |
| T7 — IndexNow completion | DONE | All matching paths classified, four protocol step variants, public GET matches, exact authorized exceptions |
| T8 — Legacy provenance | DONE | Exact introduction/copy metadata and redacted context; no generator proof, explicit synthetic-owner attestation recorded |
| T9 — Revalidation | DONE | Native history scans, 9 policy/privacy + 40 release + 3 crypto tests, six validators and lint; final privacy/preservation evidence in JSON |
| T10 — Handover | DONE | Current tables, owner evidence, local-only changes, no production authority implied |

No historical-finding owner decision remains outstanding on the supplied attestation.
Normal integration into PR #51 and hosted validation-only workflows are now authorized;
PR #51 must remain draft. Rotation, SSH/GH_TOKEN changes, history rewriting, applying
the mirror, modifying tags/protections, merging and production publication remain
unauthorized. Run hosted checks on the exact integrated commit and record their results
in the PR. Existing release-environment, signing and Partner Center administrative
prerequisites remain separate and unchanged.

**GO: integrate this historical-security patch into the existing draft PR and execute
validation-only checks. NO-GO: production promotion, history/tag changes or merging.**
Independent generator proof for the 2024
TOTP example remains unavailable and is disclosed; its local disposition rests on
the owner's explicit synthetic-data confirmation.
