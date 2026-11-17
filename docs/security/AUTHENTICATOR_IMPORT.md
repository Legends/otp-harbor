# Authenticator migration and supported fields

Settings → Data & backup → Import accounts starts with a file chooser on Desktop
and Android. The authenticator dropdown is optional export guidance, not a parser
override. Plaintext file contents identify the format; changing `.json` to `.csv`
does not change the parser. Platform adapters only select files or obtain QR data.

| Source | Supported input |
| --- | --- |
| 2FAS | Unencrypted `.2fas` / JSON, schema versions 1–4 |
| Aegis | Unencrypted JSON vault, outer version 1, database versions 1–3 (including exports from Aegis 3.4.3) |
| Bitwarden Authenticator | Unencrypted Bitwarden-compatible JSON or CSV; the Android six-header/nine-value CSV variant is supported |
| Bitwarden Password Manager | Unencrypted JSON (`encrypted: false`, `items`) or CSV (`name`, `type`, `login_totp`); login items containing TOTP only |
| LastPass Authenticator | Unencrypted version-1 JSON with `accounts`, `issuerName`, `userName`, `secret`, `algorithm`, `digits`, `timeStep` |
| Proton Authenticator | Plaintext version-1 JSON `entries`, with `content.entry_type: Totp` and `content.uri` |
| Ente Auth | Plaintext file containing one standard `otpauth://totp/` URI per line; no dedicated Ente JSON or encrypted-container parser |
| Other applications: portable text | UTF-8 standard TOTP URI lists, one URI per line; blank lines and `#` comment lines are allowed |
| Other applications: QR | Standard `otpauth://totp/` QR codes from QR images or platform camera capture |
| Google Authenticator | `otpauth-migration://` transfer QR codes, migration payload versions 1–2, including multipart exports |
| OTP Harbor: JSON | Native account-array JSON |
| OTP Harbor: CSV | Native account CSV with `id,issuer,account_name,secret` columns and supported optional fields |
| OTP Harbor: TXT | Native pipe-delimited account text, including existing legacy headerless variants |

The source-authenticator dropdown provides guidance only; an app name there is
not a claim of support for all of that app's export formats. There is no dedicated
native-export parser for Microsoft Authenticator, KeePassXC, Stratum,
Authy, 2FAGuard or Fast 2FA. Those sources can only use a supported standard TOTP
URI/QR or an explicitly compatible schema. Microsoft push approvals and passkeys
cannot be imported as TOTP accounts.

Encrypted third-party backups are unsupported, including encrypted Aegis, 2FAS,
Bitwarden, Ente and Proton containers. OTP Harbor's own password-protected
`.otphbp` backups, legacy `.totp` backups and automatic `.otphbackup` snapshots
use the same **Import accounts** entry point. Content detection identifies portable
backup headers or automatic-backup JSON/ZIP metadata independently of the filename.
The user is asked for the portable backup password, or the master password active
when an automatic snapshot was created. Wrong passwords can be retried; cancelling
or locking releases the pending operation and clears the input. Only successfully
decrypted, validated accounts enter the common new-account confirmation,
duplicate-only acknowledgement, or summary/conflict-review flow.

OTP Harbor backups still dispatch to the existing ID-aware backup merge service;
external formats use issuer + account name. Native backup review offers Replace
and Skip, retaining its established metadata semantics. Existing unrelated accounts,
current master password, authorization envelope and application settings remain.
A recovery backup precedes accepted changes and failure aborts the commit. Only
the obsolete visible Restore action was removed; reader/restore APIs remain for
compatibility and older-vault recovery. No encryption/KDF or stored schema changes.
Plain and portable encrypted inputs retain the 5 MiB limit; automatic snapshot
containers retain their existing 24 MiB limit. Format sniffing bounds ZIP manifest
inflation to 64 KiB, and temporary import buffers are zeroed on all exits.
The supported TOTP parameters are SHA-1/256/512, 6/8 digits and periods from 5 to
3600 seconds; HOTP, Steam-specific tokens, passkeys and push/SMS methods are not
converted to TOTP. Generic URI lists require the standard TOTP URI syntax;
a raw Base32-secret-only file or arbitrary third-party JSON/CSV is not enough.

## Fields and review

External file review identifies accounts by the pair of issuer and optional account
name, ignoring capitalization and surrounding whitespace. Null, empty and
whitespace-only account names represent the same unnamed account for that issuer.
Two unnamed accounts with the same issuer require review if supported values
differ; identical values are exact duplicates and are skipped. An unnamed account
and a named account are distinct, as are different issuers or different account
names. Reusing a seed or external record ID alone does not make an identity match.
Earlier entries in the same file follow the same rule. QR imports already use
issuer/account identity, including unnamed accounts.

This is a comparison-policy change only: incoming identity determines whether a
reviewed merge may target an existing account. A new identity with a colliding ID
receives a fresh ID without changing its issuer/account names. Existing vault data
is unchanged until an explicitly accepted import; backup restore retains its
separate ID-first matching and authoritative metadata behavior. Secret validation,
secret-free preview metadata, recovery backups and atomic commits are unchanged.

Supported account values are issuer, optional account name, Base32 seed, SHA-1 /
SHA-256 / SHA-512, six or eight digits, and periods from 5 through 3600 seconds.
Bitwarden's supported favorite flag is retained. Existing native OTP Harbor account
metadata continues through its established parser and merge policy. Foreign record
IDs are not used as OTP Harbor IDs. Provider-specific notes, passwords, passkeys,
attachments, display metadata and arbitrary JSON fields are not mapped into accounts.
Foreign folder/tag structures are not automatically converted into groups.
Ente URI exports may carry a `codeDisplay.trashed` flag. Entries marked as deleted
are excluded with a separate visible count, never reactivated as new accounts.
Invalid or conflicting deletion metadata rejects the file instead of guessing.
Other display metadata remains ignored. An all-deleted export shows an
informational acknowledgement without reading or writing the current accounts.
Bitwarden CSV accepts login type `login` or numeric `1`. Android Authenticator's
six-column `folder,favorite,type,name,login_uri,login_totp` header can accompany
nine-column rows, as emitted by the [upstream exporter](https://github.com/bitwarden/android/blob/6569c91e6d1a3e6ea4bd16e3070683466ea12d1c/authenticator/src/main/kotlin/com/bitwarden/authenticator/data/authenticator/repository/AuthenticatorRepositoryImpl.kt).
Only that exact header layout with numeric type `1` permits the three unlabelled
issuer/period/digits fields. They must match the declared name and validated
TOTP URI interpretation; contradictory values, wrong row widths or raw seeds in
that extended layout reject the entire file. Ordinary Bitwarden password-vault
CSV retains strict declared-column counts. A missing username column supplies no
fallback account name, matching the equivalent JSON export. No foreign password,
note, folder or additional field is imported. This is a shared desktop/Android
reader compatibility change with no vault/encryption/backup-format changes;
existing file-size, CSV syntax, TOTP validation and review-before-write boundaries
remain in force.
For Aegis exports, including Aegis 3.4.3, an empty issuer with a nonempty name is
mapped like an issuer-less OTP URI: the trimmed sole label becomes OTP Harbor's
required issuer and the account name stays empty. Explicit nonempty issuer/name
pairs are preserved. Entries with no identity, excessive identity lengths, invalid
seeds or unsupported TOTP parameters still reject the whole file. This mapping
does not alter seeds, TOTP parameters, backup restoration or existing vault data.

The destination is the current vault. There is no new profile storage or profile
selector. Account import uses the established non-authoritative merge policy;
restoring an OTP Harbor backup retains its existing authoritative account-metadata
and conflict/recovery-backup behavior. Restore does not silently replace the whole
vault or reset its master password. The import page directs encrypted OTP Harbor
backups through the same Import accounts action and a password prompt.

Files and QR imports both build a validated preview before mutation. When every
entry is new and valid, with no existing matches, within-export duplicates or
excluded entries, one short localized "Import now?" confirmation replaces the
summary and full review screens. Missing metadata or inconsistent counts cannot
take this shortcut. If every entry is an exact duplicate of an existing account,
one localized acknowledgement says "Import contains only duplicates. Nothing to
import!"; no summary/review, backup, mutation or accounts-changed event follows.
Any new, changed, excluded, unsupported or invalid entry prevents this duplicate-only
shortcut. Otherwise the existing summary modal leads to the shared
desktop review window or Android review page, showing account identities, TOTP
parameters and compatible/new/existing/invalid/unsupported/non-TOTP counts, plus
duplicates within the export. Back returns to the summary; Cancel ends the import.
No action imports accounts without confirmation. OTP Harbor backup imports retain their
ID-aware matching and supported metadata semantics within the common review UI. The existing merge planner computes new-account counts for duplicate
inputs without writing to storage. Entries in a Bitwarden
password vault that contain no TOTP are excluded with a visible count. Invalid or
unsupported TOTP entries in the new provider/URI readers reject the entire file and
report counts; no supported subset is silently committed. Unreadable documents and
the existing strict Aegis/2FAS/native format validators fail closed before account
mutation. They cannot offer an account-level preview when decoding fails.

QR review uses the same presentation as file review, replacing the old formatted
text confirmation and sequential conflict prompts. A secret-free adapter maps the
QR service's existing decisions into the shared review model. Existing and earlier
batch entries include issuer/account and algorithm/digits/period metadata, never
seeds. Google multipart handling, validated matching and atomic commit behavior
remain in the QR service. Cancellation before confirmation/review acceptance performs no write.
Existing duplicate and conflict decisions are preserved. A file with only unchanged
accounts reports that nothing needs importing; excluded non-TOTP counts remain
visible even when every TOTP account already exists.

External file imports use a dedicated resizable desktop review window and a full
Android review page, with the same secret-free presentation model. Desktop offers
category filters, search, a comparison table and bulk decisions; compact layouts
use expandable cards. Actions stay outside the scrolling account list. Every
changed account requires an explicit Replace, Keep both or Skip decision before
import can proceed. Search and filtering do not discard decisions for hidden rows.
Exact duplicates are skipped automatically and are not selectable for bulk actions.
The select-visible checkbox is disabled when the current category/search contains
no conflicts; the action dropdown requires selected conflict rows.
Back, window close and Android system Back return without committing accounts.
Android hosts a native RecyclerView for the scrolling review body, following the
same NativeControlHost composition used by the account list. Its native summary,
search, filters and expandable cards share one scroll surface; the fixed footer
remains Avalonia. The shared Avalonia review page retains a virtualized fallback
for other mobile hosts and headless tests. The fallback is disconnected on Android,
so it does not build a second hidden card list. Both presentations use the same
secret-free ImportReviewViewModel; native controls only select existing model
actions and never access seeds or commit accounts themselves. Stable import indices
identify recycled cards, and explicit decisions remain in the model when a card
leaves the viewport. Native rows have no selectable or pressed gray background;
selected conflict actions retain their own indicator.
OTP refresh is already stopped when Settings opens and is explicitly stopped again
on review entry. Hidden account rows do not receive countdown/code binding updates.
Results or exceptions from a cancelled generation are discarded; fresh current and
next codes are generated when the user returns to the account list. This does not
change TOTP parameters, authorization, import decisions or stored vault data.

External review compares matching issuer/account identities and repeated entries
within the incoming file; a shared seed alone does not make an identity match. This
comparison stays in the shared import service; neither full seeds nor masked seed
fragments enter the review model. Current/imported identity, algorithm, digits,
period and changed-field flags provide the comparison. Replace retains the matched
account ID; Keep both uses the existing collision-safe account creation policy.
Review targets stay bound to the compared vault record or earlier incoming entry
even after a preceding decision changes its seed or label. Decisions apply in
export order; later replacements can update that same record. An earlier incoming
entry is labelled as such, rather than presented as an existing vault account.
Encrypted OTP Harbor backups share the import review UI and retain ID-aware matching.

Failed provider decoding can supply counts without safe account identities. The
blocked review shows those counts and an explanation rather than inventing rows or
exposing malformed secret values. Non-TOTP entries are reported as exclusions;
an invalid or unsupported TOTP file cannot be partially imported from this view.

## Security impact and compatibility

This change adds plaintext-format recognition, not foreign-export decryption.
Encrypted provider exports and unknown Proton versions are rejected. Contradictory
encryption metadata and duplicate fields in the new JSON schemas are rejected.
The existing encrypted OTP Harbor backup envelopes, cryptographic parameters,
authorization rules and file formats are unchanged.

Plaintext and portable encrypted inputs retain the existing 5 MiB byte limit;
automatic snapshots retain their 24 MiB container limit. Plaintext uses strict UTF-8 decoding. Parsing runs
off the UI thread. Temporary input byte buffers are cleared in the existing finally
block. Seed strings have the same lifetime constraints as the existing Account
model; failed provider parses return count metadata only. Preview records contain
no seed or foreign password fields. Logs contain fixed operation/type/count data,
never raw export contents or parser-provided account values.

Synthetic tests cover content/extension mismatches, provider schema versions,
encryption markers, malformed and unsupported OTP entries, non-TOTP counts,
parameter preservation, secret-free previews and review cancellation before writes.
Shared UI tests exercise all source choices, supported actions, four locales,
light/dark themes and compact viewports.

## Upstream format references

- [2FAS migration/export instructions](https://2fas.com/support/2fas-auth-mobile-app/i-want-to-move-copy-transfer-tokens-codes-between-ios-and-android/)
- [Aegis vault format](https://github.com/beemdevelopment/Aegis/blob/master/docs/vault.md)
- [Google Authenticator transfers](https://support.google.com/accounts/answer/1066447)
- [Bitwarden import/export documentation](https://bitwarden.com/help/authenticator-import-export/)
- [Bitwarden Authenticator exporter](https://github.com/bitwarden/android/blob/main/authenticator/src/main/kotlin/com/bitwarden/authenticator/data/authenticator/repository/AuthenticatorRepositoryImpl.kt)
- [Ente export format and encrypted-export guidance](https://ente.com/help/auth/migration/export)
- [Proton Authenticator versioned export model](https://github.com/protonpass/proton-pass-common/blob/main/proton-authenticator/src/entry/exporter.rs)

These are format references, not claims of affiliation. Test exports are synthetic;
no real user export or upstream account seed is included in the repository.

## Content dispatch, ambiguity and the selected-file summary

`AccountImportDispatcher` owns content recognition and dispatch, with a registry
for provider JSON signatures and existing provider-specific parsers. Native
JSON/CSV/text, Bitwarden CSV and standard URI lists use the same bounded dispatch
path. Selected help text, file suffixes and Android MIME types do not choose the
parser. The external file picker allows arbitrary names, including misleading
backup suffixes. Recognized OTP Harbor backup headers open a password prompt;
only successfully decrypted accounts proceed to common import review. The existing
native readers and ID-aware restore service retain their password validation,
authoritative metadata and encrypted-envelope behavior internally.

The selected-file summary modal contains only a sanitized filename, localized format-family
label and validation counts. Bitwarden-compatible JSON/CSV and standard URI lists
do not claim a unique originating application. The Account objects stay in the
host's pending operation. A read-only vault preview classifies the clean,
duplicate-only or review-required route; no account write occurs before explicit
import acceptance. Desktop uses an owned modal window; Android uses
a centered modal overlay that also handles system Back. Review closes this modal
before opening the existing conflict-resolution window/page and still delegates
to AccountImportService. No parser, backup format, vault storage or authorization
semantics change in this presentation step.
Back returns to this summary. On desktop, Cancel or closing the conflict window
ends the import instead of reopening the summary. Neither action commits accounts.
Clearing the file or locking/closing/disposal releases
the pending operation. Android uses its existing sensitive-operation cancellation.

If distinct supported schemas are recognized, the stream reader awaits an explicit
choice among those candidates before invoking a parser. This handles non-seekable
Android document streams without reopening or storing the export. The format-specific
5 MiB plain/portable and 24 MiB automatic-container limits remain in force,
with strict UTF-8 decoding for plaintext; input byte buffers are
cleared in the service finally block after selection, cancellation or failure.
Unknown data, duplicate schema fields, malformed input, encrypted foreign exports,
and a choice outside the recognized candidates fail closed without trying other
parsers. An API caller without a format-selection callback receives an ambiguity
error. Tests exercise renamed extensions, non-seekable streams, each recognized
family, candidate choices/cancellation, unchanged backup behavior, file-summary
staging and the existing conflict/commit pipeline.

## Upstream evidence

See [the upstream format audit](AUTHENTICATOR_EXPORT_FORMAT_AUDIT.md) for verified repository documentation, serialization models and migration limitations. Format compatibility does not imply affiliation.
The [Ente/2FAS source comparison](AUTHENTICATOR_IMPORT_PARITY.md) records the
additional plaintext fixes, encrypted-format audit and regression provenance.
