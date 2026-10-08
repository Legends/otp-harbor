# OTP Harbor Icon Pack Import Contract

Status: normative application-side contract for `.otphicons` format version 1.

Audience: OTP Harbor maintainers and agents implementing or changing
`OtpHarbor.IconPackBuilder`.

This document defines what the OTP Harbor application accepts, preserves, and
uses. The builder owns provider acquisition and provider-specific parsing. The
application does not download or reinterpret Aegis Icons, Dashboard Icons, or
Simple Icons data.

## Compatibility boundary

- File extension: `.otphicons`.
- Container: ZIP.
- Root manifest: `pack.json`.
- `formatVersion`: `1`.
- `packId`: `otp-harbor-icons`.
- Normal import UI selects `*.otphicons`; Android document providers may expose
  the file with a generic or ZIP MIME type.
- A file with the `.otphicons` extension is claimed by this importer. If it is
  malformed, OTP Harbor rejects it and does not reinterpret it as a legacy
  provider archive.
- Unknown JSON members, comments, trailing commas, wrong casing, unsupported
  format versions, and ambiguous or incomplete data are rejected. A format
  extension therefore requires a new `formatVersion` and coordinated app and
  builder changes.

The standalone builder/importer is a separate product boundary. None of its
provider acquisition, provider parsing, source selection, SVG normalization,
color extraction, merging, or package-production implementation may be copied
into or referenced by OTP Harbor. The application owns only validation and
consumption of the provider-neutral format. Legacy application import support,
where retained for migration compatibility, is not part of the standalone
builder/importer and must not be extended with new provider behavior.

## Required archive layout

```text
pack.json
icons/<canonical-brand-id>.svg
licenses/<provider>/<license-file>
```

Every non-directory archive entry must be referenced by `pack.json`. Every
referenced icon and license must exist exactly once. OTP Harbor rejects duplicate
entry names case-insensitively, unsafe paths, case-mismatched canonical paths,
missing files, and unreferenced files.

Paths must be relative, use `/`, contain no NUL, and contain no `.` or `..` path
segment. Absolute, rooted, and traversal paths are forbidden.

## Required manifest semantics

The builder schema in `schemas/otp-harbor-icon-pack.schema.json` defines the
serialized JSON shape. The following application semantics are additionally
normative.

### Pack sources

Each `sources` item must contain:

- a unique lowercase kebab-case `provider`;
- a base filename in `inputFileName`, not a path;
- a lowercase 64-character SHA-256 digest in `sha256`;
- `metadata`, which may contain null or empty values;
- `licenseFiles`, including every notice required for redistribution.

Optional `version` and `revision` values must be printable text. An optional
`sourceUrl` must be an absolute HTTPS URL. Every license path must begin with
`licenses/<provider>/`.

### Brands

Each brand must contain:

- a unique lowercase kebab-case `id`;
- a printable `displayName`;
- an uppercase `#RRGGBB` `backgroundColor`;
- `icon` equal to `icons/<id>.svg`;
- at least one original `issuerAliases` value;
- one `selectedSource`;
- one or more unique contributing `sources`.

`selectedSource` must also occur in the contributing `sources` list. Every
source reference must name a provider declared at pack level. Provider-specific
metadata is preserved; an empty string is valid metadata and must not be removed
or treated as a validation failure.

Canonical IDs are stable external identifiers. The app persists and resolves
them verbatim; the builder must not depend on the app recanonicalizing them.

### Issuer alias index

The top-level `issuerAliases` array is authoritative. Each entry maps one
normalized `key` to an existing `brandId`.

The builder must include exactly the complete, non-ambiguous normalized mapping
derived from every brand's original `issuerAliases`. Missing, extra, duplicate,
non-normalized, unknown-brand, or conflicting mappings reject the whole pack.
There is no arbitrary product limit on the number of brands, original aliases,
or normalized aliases; acceptance is governed by the resource budgets below.

Format-v1 normalization is:

1. Unicode normalize with Form KC.
2. Convert each Unicode rune to lowercase invariant form.
3. Keep letters, digits, `+`, `&`, math symbols, currency symbols, and other
   symbols.
4. Replace punctuation, separators, controls, formatting characters, combining
   marks, surrogates, and private-use or unassigned characters with a separator.
5. Collapse separator runs to one ASCII space and trim leading/trailing spaces.

The builder and app must share conformance vectors for this algorithm. Changing
it is a format compatibility change.

## Matching behavior

- Automatic matching uses the mandatory TOTP issuer only.
- Account name or label is not an automatic matching input.
- An explicit per-account icon choice may override automatic matching.
- When an authoritative format-v1 alias index is installed, a lookup miss stays
  a miss; the app does not apply fuzzy legacy fallback matching.
- Resolution is entirely local. Importing a pack or resolving an issuer performs
  no network request.

## Canonical SVG rendering profile

OTP Harbor is a bounded vector-path consumer, not a browser or a provider SVG
normalizer. The standalone builder/importer must convert the selected upstream
asset into the canonical profile below before packaging it. Provider-specific
SVG interpretation must never be moved into the application.

Every packaged SVG must satisfy all of the following:

- it is well-formed XML with an `<svg>` root;
- DTD processing and external XML resolution are not needed;
- it has a finite, positive `viewBox`; the builder must derive one from numeric
  width/height when the upstream asset omits it;
- it contains at least one `<path>` with a non-empty `d` attribute;
- it contains neither `<script>` nor `<foreignObject>`;
- all reusable local geometry has been expanded, so no `<use>`, `href`, or
  `xlink:href` remains;
- circles, ellipses, rectangles, polygons, and polylines needed for the visible
  logo have been converted to equivalent path geometry;
- CSS selectors and classes have been resolved; visible path paint is either an
  explicit safe solid fill or intentionally omitted to request OTP Harbor's
  standard foreground brush;
- gradients, patterns, masks, filters, clipping, strokes, and opacity are either
  deterministically flattened to equivalent supported path layers or cause the
  builder to select a compatible alternate source; they must not be silently
  discarded;
- transforms are flattened into path coordinates when practical; otherwise
  only finite SVG `matrix`, `translate`, `scale`, `rotate`, `skewX`, and `skewY`
  transforms may remain;
- it does not require a network, file, data-URI, or other external reference.

The application renderer also understands direct and inline-style path fills,
inherited `<svg>`/`<g>` fills and solid strokes, standard finite transforms,
numeric width/height, and three- or six-digit hex paint as compatibility
behavior. The builder must not rely on that leniency for newly generated packs:
it must emit the canonical profile.

### Background and foreground semantics

`backgroundColor` is the final OTP Harbor tile surface, not merely the first
color encountered in an SVG. The builder/importer must determine it together
with the normalized foreground so the combination reproduces the selected
logo and remains visible:

- A full-logo background circle or rectangle may be represented by
  `backgroundColor` and removed from the normalized foreground only when the
  resulting tile is visually equivalent at icon size.
- A monochrome glyph may omit its fill to request the application's standard
  high-contrast foreground on the selected brand background.
- Explicit multicolor foreground layers retain their selected-source colors.
  Their tile background must be a source-backed or neutral contrasting color;
  it must not make required foreground layers disappear.
- Inline `style="fill:..."`, inherited fills, and provider CSS must be resolved
  before color analysis. Reading only a direct `fill` attribute is nonconformant.
- Color metadata from a non-selected provider must not be paired with a
  different selected logo unless an explicit reviewed mapping authorizes that
  combination.
- `#334155` is only a final contrast-checked fallback. It must not replace a
  usable background embedded in or documented for the selected source.

The builder must render-test every normalized result at OTP Harbor tile size.
This is a zero-unresolved-failure release gate, not a sample check. For every
brand, compare a trusted render of the selected upstream source with the final
normalized SVG composed on the manifest background. Verify the visible alpha
mask, bounds, layer order, required solid colors, and final background. The
builder must reject or choose another source when any result is blank, clipped,
effectively monochrome due to lost paint, materially different from its selected
source, or has required foreground paint indistinguishable from its background.
It must emit a machine-readable per-brand report outside the `.otphicons`
archive. A release pack is conformant only when that report contains zero
unresolved visual failures.

## Security resource budgets

These are denial-of-service boundaries, not desired content targets. The builder
should support as many brands as fit safely within them and must not introduce a
smaller arbitrary brand or alias cap.

| Resource | Format-v1 application limit |
| --- | ---: |
| Compressed input copied by the application | 256 MiB |
| ZIP entries | 100,000 |
| Total expanded bytes | 512 MiB |
| Per-entry expansion ratio | 200:1 |
| `pack.json` | 128 MiB |
| JSON nesting depth | 32 |
| One SVG | 1 MiB |
| Declared pack sources | 16 |
| Contributing sources per brand | 32 |
| Metadata pairs per metadata object | 64 |
| Metadata key | 128 characters |
| Metadata value | 4,096 characters; null and empty allowed |
| One license file | 1 MiB |
| All license files | 8 MiB |
| License files per source | 64 |
| Normalized alias key | 512 characters |

Text identity fields are bounded printable text, normally at 256 characters.
Integer arithmetic, stream reads, and expanded-byte accounting must fail closed
on overflow or limit violations. Do not remove all resource budgets: that would
turn a local import into an unbounded memory/disk consumption primitive.

## Transaction and failure semantics

Validation is all-or-nothing:

1. Copy and hash the selected archive into a bounded temporary file.
2. Parse and validate the container, complete manifest graph, licenses, aliases,
   provenance, and every selected SVG.
3. Write the normalized installed representation into a restricted staging
   directory.
4. Move the completed staging directory into place and atomically update the
   active registration/pointer.
5. Remove temporary and inactive prior data on a best-effort basis only after a
   successful activation.

If any brand, alias, license, provenance record, SVG, path, or resource budget is
invalid, the new pack is not activated. The previously installed pack remains
active and usable. OTP account data and explicit account-to-icon choices are not
deleted by a failed pack import.

The app stores an SHA-256 digest of the imported archive with the installed
metadata and restricts installed files to the current user through the platform
file-security abstraction.

## Preservation requirements

The application preserves and consumes:

- canonical brand IDs and display names;
- selected SVG bytes and background colors;
- original aliases and the authoritative normalized alias index;
- selected-source and contributing-source provenance;
- pack source filename, digest, version/revision, source URL, and metadata;
- referenced license and notice files;
- the imported archive SHA-256 digest.

The builder must not omit required provenance or license files merely because
the app does not currently display every field.

## Third-party rights boundary

Format compatibility does not mean affiliation, sponsorship, endorsement,
hosting, bundling by OTP Harbor releases, or a grant of rights to an icon. The
builder must preserve upstream licenses, notices, source and guideline URLs, and
per-icon license metadata when available. Third-party assets remain subject to
their own copyright, trademark, license, and usage terms. Users are responsible
for compliant use.

[Simple Icons explicitly warns](https://github.com/simple-icons/simple-icons/blob/develop/DISCLAIMER.md)
that its project-level CC0 status does not imply that every included brand icon
is CC0 and that trademark and brand-guideline rights may still apply.
[Dashboard Icons uses Apache-2.0](https://github.com/homarr-labs/dashboard-icons/blob/main/LICENSE)
for its collection and requires its license/notice conditions to be respected;
Apache-2.0 does not grant trademark permission.
[Aegis documents icon-pack format compatibility](https://github.com/beemdevelopment/Aegis/blob/master/docs/iconpacks.md),
not rights in every community pack. Unresolved or jurisdiction-specific rights
questions require qualified legal counsel.

## Security review record

Threat impact: `.otphicons` is untrusted local input. The primary risks are ZIP
bombs, path traversal, duplicate-path ambiguity, oversized metadata, malicious
XML, executable or external SVG content, poisoned alias graphs, and activation
of a partially validated pack. The validation and transaction rules above fail
closed for each of these classes while accepting bounded internal SVG fragments.

Data flow: the user-selected file is copied to a bounded temporary file while
being SHA-256 hashed; the importer validates the archive and constructs a
provider-neutral in-memory result; the service writes only validated icons,
licenses, provenance, and an index to a current-user-restricted staging
directory; an atomic registry replacement activates the completed pack. No OTP
seed, account name, password, derived key, or other vault secret enters this
flow. Icon resolution subsequently reads only local installed data.

Compatibility and migration impact: format version 1 is additive to legacy
installed-pack support. Existing account records and explicit icon overrides are
unchanged. The normal picker now selects `.otphicons`; malformed files with that
extension cannot fall back to permissive legacy parsing. Unsupported future
versions require coordinated builder/app work and do not partially import.

Verification evidence: deterministic unit tests cover schema and graph
validation, normalization, internal/external SVG references, SVGs above the old
64 KiB ceiling, provenance/license persistence, issuer-only matching, atomic
replacement failure, localization, and DI registration. The compatibility test
also accepts a real generated package containing more than 5,000 brands.

## Builder-agent conformance checklist

Before emitting a release pack, the builder implementation or agent must:

1. Validate its output against the format-v1 JSON schema.
2. Reopen the completed ZIP and verify every manifest reference; recompute each
   declared source digest against the exact input used to build the pack.
3. Apply the exact format-v1 issuer normalizer and prove the top-level alias
   index is complete and unambiguous.
4. Select icons without using account names and emit stable canonical IDs.
5. Apply the SVG profile above, including support for safe resolved local
   fragments and rejection of executable or external content.
6. Include all required upstream licenses/notices and provenance without
   claiming that OTP Harbor grants third-party rights.
7. Stay within the security resource budgets without imposing a smaller
   arbitrary brand/alias count.
8. Run builder unit/schema tests and the OTP Harbor compatibility test with
   `OTP_HARBOR_UNIFIED_ICON_PACK` pointing to the generated `.otphicons` file.
9. Treat any format or normalization change as coordinated versioned work; do
   not silently extend format version 1.

Application conformance is covered by `IconPackImporterTests`,
`IssuerAliasResolverTests`, `SimpleIconsBrandIconPackServiceTests`, and the
environment-gated `IconPackCompatibilityTests.LatestOtpHarborIconPack_IsAcceptedByProductionImporter`.
