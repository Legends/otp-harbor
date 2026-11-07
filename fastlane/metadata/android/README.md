# Android store metadata

This directory is the canonical source for Android listing text and the committed image mirrors
used by F-Droid-compatible tooling. The English images are generated from reviewed v3
synthetic-data captures by `scripts/marketing/Generate-MarketingAssets.ps1`; their manifest-backed
canonical files live under `artifacts/marketing/store/android` and may also be uploaded to Google
Play.

Listing text is maintained for every language supported by the app. English screenshots are shared
across locales until a complete, reviewed capture set exists for each language. Do not add screenshots
containing real accounts, OTP seeds, production OTP codes, recovery material, personal notifications,
or device identifiers. Synthetic test accounts and codes must be visually reviewed before commit.

The presence of this metadata does not mean OTP Harbor is available from F-Droid. Publication status
and remaining source-build gates are documented in `docs/android/FDROID_READINESS.md`.

For every Android release, add localized release notes under each locale's
`changelogs/<versionCode>.txt` (1-500 characters). Use the numeric version code from
`Get-AndroidReleaseVersion.ps1`, not the display version. These notes are shared by the
maintainer's binary repository and future store submissions.
