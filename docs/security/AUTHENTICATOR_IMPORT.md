# Authenticator migration and supported fields

Settings → Data & backup → Import accounts starts with a file chooser on Desktop
and Android. The authenticator dropdown is optional export guidance, not a parser
override. Plaintext file contents identify the format; changing `.json` to `.csv`
does not change the parser. Platform adapters only select files or obtain QR data.

| Source | Supported input |
| --- | --- |
| 2FAS | Unencrypted `.2fas` / JSON, schema versions 2–4 |
| Aegis | Unencrypted JSON vault, outer version 1, database versions 1–3 |
| Google Authenticator | Transfer QR codes, including multipart exports |
| Bitwarden Authenticator / Password Manager | Unencrypted JSON with `encrypted: false` and `items`, or CSV with `login_totp`; login items with a TOTP only |
| Ente Auth | Plaintext `otpauth://` URI list; encrypted Ente containers are rejected |
| Proton Authenticator | Plaintext version-1 JSON `entries`, with `content.entry_type: Totp` and `content.uri` |
| Other applications | Standard TOTP QR codes / UTF-8 URI lists and existing OTP Harbor JSON, CSV and pipe-delimited text account exports |
| Microsoft Authenticator | A migration guide to re-enrol standard TOTP at each service; no backup importer or conversion of push approvals/passkeys |
| KeePassXC | Guidance for standard TOTP QR/URI migration; no dedicated KeePassXC CSV parser |

## Fields and review

Supported account values are issuer, optional account name, Base32 seed, SHA-1 /
SHA-256 / SHA-512, six or eight digits, and periods from 5 through 3600 seconds.
Bitwarden's supported favorite flag is retained. Existing native OTP Harbor account
metadata continues through its established parser and merge policy. Foreign record
IDs are not used as OTP Harbor IDs. Provider-specific notes, passwords, passkeys,
attachments, display metadata and arbitrary JSON fields are not mapped into accounts.
Foreign folder/tag structures are not automatically converted into groups.

The destination is the current vault. There is no new profile storage or profile
selector. Account import uses the established non-authoritative merge policy;
restoring an OTP Harbor backup retains its existing authoritative account-metadata
and conflict/recovery-backup behavior. Restore does not silently replace the whole
vault or reset its master password. The import page directs encrypted OTP Harbor
backups to the separate restore action.

Before mutation, file imports show account identities and TOTP parameters plus
compatible/new/existing/invalid/unsupported/non-TOTP counts, plus duplicates within
the export. The existing merge planner computes new-account counts for duplicate
inputs without writing to storage. Entries in a Bitwarden
password vault that contain no TOTP are excluded with a visible count. Invalid or
unsupported TOTP entries in the new provider/URI readers reject the entire file and
report counts; no supported subset is silently committed. Unreadable documents and
the existing strict Aegis/2FAS/native format validators fail closed before account
mutation. They cannot offer an account-level preview when decoding fails.

QR review includes supported metadata before adding a new account or committing a
Google migration batch. Cancellation before review acceptance performs no write.
Existing duplicate and conflict decisions are preserved. A file with only unchanged
accounts reports that nothing needs importing; excluded non-TOTP counts remain
visible even when every TOTP account already exists.

External file imports use a dedicated resizable desktop review window and a full
Android review page, with the same secret-free presentation model. Desktop offers
category filters, search, a comparison table and bulk decisions; compact layouts
use expandable cards. Actions stay outside the scrolling account list. Every
changed account requires an explicit Replace, Keep both or Skip decision before
import can proceed. Search and filtering do not discard decisions for hidden rows.
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

External review also compares entries that share a normalized seed but have
different labels, and compares repeated entries within the incoming file. This
comparison stays in the shared import service; neither full seeds nor masked seed
fragments enter the review model. Current/imported labels, algorithm, digits,
period and changed-field flags provide the comparison. Replace retains the matched
account ID; Keep both uses the existing collision-safe account creation policy.
Review targets stay bound to the compared vault record or earlier incoming entry
even after a preceding decision changes its seed or label. Decisions apply in
export order; later replacements can update that same record. An earlier incoming
entry is labelled as such, rather than presented as an existing vault account.
Encrypted OTP Harbor restore retains its separate review and matching semantics.

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

Inputs retain the existing 5 MiB byte limit and strict UTF-8 decoding. Parsing runs
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
- [Bitwarden Authenticator export model](https://github.com/bitwarden/authenticator-android/blob/main/authenticator/src/main/kotlin/com/bitwarden/authenticator/data/authenticator/manager/model/ExportJsonData.kt)
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
backup suffixes; the separate backup restore API retains its existing password
and encrypted-envelope behavior. A recognizable binary OTP Harbor envelope in the
external importer directs the user to Restore backup and is never decrypted there.

The selected-file summary modal contains only a sanitized filename, localized format-family
label and validation counts. Bitwarden-compatible JSON/CSV and standard URI lists
do not claim a unique originating application. The Account objects stay in the
host's pending operation; no vault lookup, conflict decision or account write
occurs until Review is selected. Desktop uses an owned modal window; Android uses
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
Android document streams without reopening or storing the export. The original
5 MiB bound and strict UTF-8 decoding remain in force; input byte buffers are
cleared in the service finally block after selection, cancellation or failure.
Unknown data, duplicate schema fields, malformed input, encrypted foreign exports,
and a choice outside the recognized candidates fail closed without trying other
parsers. An API caller without a format-selection callback receives an ambiguity
error. Tests exercise renamed extensions, non-seekable streams, each recognized
family, candidate choices/cancellation, unchanged backup behavior, file-summary
staging and the existing conflict/commit pipeline.
