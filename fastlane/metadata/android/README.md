# Android store metadata

This directory is the canonical source for the Android listing text and store screenshots used by
F-Droid-compatible tooling. The English images are generated from reviewed synthetic-data captures
by `scripts/assets/New-AndroidMarketingImages.ps1`; they may also be uploaded to Google Play.

Listing text is maintained for every language supported by the app. English screenshots are shared
across locales until a complete, reviewed capture set exists for each language. Do not add screenshots
containing real accounts, OTP seeds, production OTP codes, recovery material, personal notifications,
or device identifiers. Synthetic test accounts and codes must be visually reviewed before commit.

The presence of this metadata does not mean OTP Harbor is available from F-Droid. Publication status
and remaining source-build gates are documented in `docs/android/FDROID_READINESS.md`.
