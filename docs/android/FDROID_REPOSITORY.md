# OTP Harbor repository for F-Droid

OTP Harbor's maintainer-operated binary repository is separate from the official F-Droid
catalog. Users add it to their F-Droid client to install and update the same production-signed
APKs published on GitHub. It does not imply F-Droid's review, source rebuilding, or endorsement.
Official-catalog inclusion remains tracked in [F-Droid readiness](FDROID_READINESS.md).

Public endpoints after successful deployment:

- Setup guide: https://legends.github.io/otp-harbor/fdroid/
- Repository: https://legends.github.io/otp-harbor/fdroid/repo

The guide renders the repository URL and SHA-256 certificate fingerprint from
`packaging/fdroid/repository.json`. The repository-index certificate and APK certificate are
different identities. Users verify the index certificate when adding the repository; Android
continues verifying the existing production APK certificate when installing updates.

## Publication

The website workflow calls `fdroid-repository.yml` before assembling the Pages artifact. A
failed repository verification prevents the entire new deployment, leaving the existing website
and repository in place. Android release publication already dispatches the website workflow;
the weekly schedule refreshes the signed index between releases.

The generator:

1. Selects the two newest published, stable `android-v` releases by version code. Drafts,
   prereleases and desktop tags are excluded.
2. Downloads only their exact GitHub release APKs and checksums over HTTPS.
3. Verifies SHA-256, the pinned production APK certificate, application ID, release version/code
   and absence of the debuggable flag. It never signs or modifies APKs.
4. Imports the shared localized Fastlane metadata and generates the index with pinned
   `fdroidserver` 2.4.5 tooling.
5. Verifies the signed index with F-Droid's own verifier and the separately pinned index
   certificate, and checks that package hashes match the verified APKs.
6. Uploads only the `repo` directory. Signing/configuration files remain in a private temporary
   workspace outside the checkout and are removed after the job, including on failure.

No unsigned, debug, locally rebuilt or unreviewed APKs are distributed by this workflow.
Repository history retains two versions; older releases remain available on GitHub.

## Signing identity and recovery

The index key is separate from the GitHub/Play APK key. Initialize it once with
`scripts/release/Initialize-FDroidRepositorySigning.ps1` on Windows, using the JDK's `keytool`.
The initializer rejects an existing signing directory and any directory inside the checkout.
It restricts the directory to the current user and SYSTEM, creates an encrypted PKCS#12 keystore,
and protects the local keystore password with current-user Windows DPAPI.

The default private directory is `%LOCALAPPDATA%/OTP-Harbor/fdroid-repository-signing`.
Keep a secure backup of the signing identity and arrange password recovery before replacing this
Windows profile; the DPAPI password file is tied to the current Windows user. Losing the index
key prevents transparent repository updates for existing subscribers. Do not regenerate it for
each release or commit any private signing file.

`Set-FDroidRepositorySecrets.ps1` uploads the index keystore/password through standard input to
the dedicated `fdroid-repository` GitHub environment, restricted to `master`. Neither secret is
passed in command arguments. The production APK key is never accessed or uploaded by these tools.
An initial presence check verifies that the named environment secrets were forwarded to the
reusable workflow. Private key files are materialized only in the signing step, after installing
tools and testing the safety boundaries. The public certificate fingerprint is committed for
verification; no repository-wide secret inheritance is used.

## Compatibility and future stores

GitHub and this binary repository distribute identical APKs with the same production signing
identity, allowing compatible in-place updates. Users should keep encrypted backups before
changing installation sources. Debug packages use a different application ID. A future official
F-Droid package would initially use F-Droid-managed signing and require the migration in
[signing and updates](FDROID_SIGNING_AND_UPDATES.md).

Google Play remains a separate future distribution step: enroll the existing app-signing identity
according to the [Android release guide](../release/ANDROID.md), produce the required store bundle,
and complete Play Console review. This repository does not enroll in Play or alter the production
identity, vault format, backup format, app permissions, or runtime code.

## Security review and verification

- Threat impact: index signing authenticates the catalogue; checksum and pinned APK-certificate
  validation independently restrict its application contents. CI signing still trusts the pinned
  F-Droid tooling and its dependencies; protect the default branch and environment accordingly.
- Data flow: only public releases, checksums, metadata, artwork and signatures are processed.
  No device data, accounts, vaults, passwords, OTP seeds or private APK signing material are read.
- Compatibility: existing production APKs and signatures are preserved. A custom repository does
  not satisfy the separate official-catalog source-build acceptance requirements.
- Verification: unit tests cover selection, version ordering, checksums, untrusted URLs, identity,
  certificates, debug APK rejection, public-file isolation and guide fingerprint rendering.
  Linux generation and deployed signed-index verification must pass before availability is claimed.

Verified on 2026-11-07: [website workflow 42352046443](https://github.com/Legends/otp-harbor/actions/runs/42352046443)
generated and published two production versions. Independent public-endpoint checks verified the
signed index/certificate, both APK sizes, and a complete v3.0.2 download against the index SHA-256
and production APK certificate. Adding the repository and installing through a physical F-Droid
client remains a manual check.

References: [F-Droid repository setup](https://f-droid.org/docs/Setup_an_F-Droid_App_Repo/),
[signing process](https://f-droid.org/docs/Signing_Process/), and
[server tooling](https://pypi.org/project/fdroidserver/).
