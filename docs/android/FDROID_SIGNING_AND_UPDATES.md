# F-Droid signing and update channels

OTP Harbor is not yet available from the official F-Droid repository. If the source-build and
review gates pass, the initial F-Droid package will use **F-Droid-managed signing**. OTP Harbor's
GitHub/Google Play production signing key will not be uploaded or disclosed to F-Droid.

This policy follows F-Droid's documented
[signing process](https://f-droid.org/docs/Signing_Process/) and keeps the alternative
[reproducible upstream-binary process](https://f-droid.org/docs/Reproducible_Builds/) unavailable
until OTP Harbor has independent reproducibility evidence.

## Channel identities

| Channel | Builder | Signer | Update owner |
| --- | --- | --- | --- |
| GitHub Releases | OTP Harbor CI | OTP Harbor production app-signing key | User downloads a verified APK manually. |
| Google Play, if enabled | OTP Harbor/Play build pipeline | The enrolled OTP Harbor app-signing identity | Google Play. |
| Official F-Droid, if accepted | F-Droid build infrastructure from tagged source | F-Droid-managed key | The user's F-Droid client. |

Every production channel uses application ID `io.github.legends.otpharbor` and the same monotonically
increasing version-code formula. Matching package names do not make different signatures compatible:
Android will reject an in-place install from a differently signed channel.

## Switching channels safely

Before uninstalling an existing production package:

1. Export a password-protected encrypted `.totp` backup through OTP Harbor.
2. Verify the backup password and keep it separately from the backup file.
3. Keep the original installation until the external backup is present and readable.
4. Uninstall the old channel only after accepting that Android will delete its private app data.
5. Install the target channel, create or unlock its new local vault, and choose **Restore encrypted backup**.
6. Review conflict handling before restore, then verify accounts and a second unlock before removing the backup.

Android system backup is disabled, so uninstalling without an external encrypted backup destroys the
only local vault copy. There is no cross-signature upgrade bypass and no server-side account recovery.

## Signing controls

- Never provide the GitHub/Play keystore, key password, store password, or encoded key material to
  F-Droid, its metadata repository, issue trackers, logs, or support channels.
- F-Droid builds must originate from immutable public tags and use the same reviewed source and
  version mapping as other Android channels.
- Do not add an in-app updater or network permission. Each package manager owns updates for its
  installed signature.
- Do not publish the same version code with different application contents across production channels.
- Public download pages must identify the channel and warn that switching signatures requires the
  encrypted-backup migration above.

## Future upstream-signed option

An upstream-signed APK may be considered only after two clean independent environments and the
proposed F-Droid recipe reproduce the full non-signature payload. F-Droid must then verify the
upstream artifact using its reproducible-build flow and restrict the accepted signing certificate.
No private signing material is transferred in that process. Until all of those controls are proven,
F-Droid-managed signing remains the policy and cross-channel in-place updates remain unsupported.

## Security and compatibility review

- **Threat impact:** separate keys prevent a compromise of F-Droid infrastructure from authorizing a
  GitHub/Play update and prevent disclosure of OTP Harbor's long-lived production key. Users must be
  protected from destructive cross-channel migration mistakes.
- **Data-flow impact:** stores and package managers handle public binaries and signatures only. Vaults,
  passwords, OTP seeds, and encrypted backups are never supplied to a distribution service.
- **Compatibility impact:** vault and `.totp` backup formats remain shared, but Android signature
  enforcement requires uninstall/reinstall when moving between F-Droid and GitHub/Play.
- **Verification evidence:** repository validation checks the permanent package ID, disabled Android
  backup, explicit separate-signature policy, encrypted migration workflow, and absence of premature
  F-Droid availability claims in source listing text.
