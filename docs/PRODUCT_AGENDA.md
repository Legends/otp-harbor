# OTP Harbor product agenda

This is the maintained product agenda. Security, recovery confidence, local-first behavior, and
license compliance take priority over convenience or distribution reach.

1. **Issuer and logo handling — in progress.** Keep third-party artwork outside OTP Harbor
   distributions while improving issuer resolution for user-imported local icon packs and custom
   per-account icons.
2. **Import and export workflow — implemented baseline, continue hardening.** Keep migration from
   Aegis, Google Authenticator, 2FAS, URI lists, and OTP Harbor backups explicit, recoverable, and
   well-tested.
3. **Large-vault navigation — planned.** Improve filtering, grouping, tags, favorites, and
   navigation for vaults containing hundreds of accounts.
4. **Keyboard shortcuts — implemented baseline.** Preserve fast search, code copying, accessible
   focus order, and near mouse-free desktop operation.
5. **First-run and onboarding — implemented baseline, continue refinement.** Explain vault
   creation, encryption, backups, and recovery without overwhelming new users.
6. **UI consistency — ongoing.** Continue reviewing spacing, dialogs, context menus, focus,
   confirmations, accessibility, and resizing behavior.
7. **Backup UX — planned.** Make backup locations, encryption status, recovery requirements, and
   restore workflows unmistakable.
8. **Cross-platform consistency — ongoing.** Keep Windows, Linux, Android, and eventually macOS
   behavior and appearance coherent; macOS implementation work is currently deferred.
9. **F-Droid-compatible Android distribution — planned with a license gate.** Add reproducible
   Android release builds, F-Droid metadata, deterministic dependency verification, signing and
   update documentation, and an independently auditable publishing workflow. Under the current
   PolyForm Noncommercial license, target a project-operated F-Droid-compatible repository rather
   than the official F-Droid main repository. Official main-repository submission requires a
   deliberate Android licensing decision because its FLOSS requirement conflicts with the project's
   prohibition on commercial forks.

## F-Droid acceptance gate

Do not claim that OTP Harbor is available from the official F-Droid repository until it has been
accepted there. Before implementation, choose and document one of these mutually exclusive paths:

- retain PolyForm Noncommercial and publish through a project-operated F-Droid-compatible
  repository; or
- separately license the complete Android application under an F-Droid-accepted FLOSS license and
  explicitly accept that the license can permit commercial reuse and forks.

Either path requires a clean source build without proprietary dependencies, reproducibility checks,
reviewed metadata and screenshots, a protected repository-signing key, documented update recovery,
and release validation that remains independent from Google Play and GitHub APK signing.
