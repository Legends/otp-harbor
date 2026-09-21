# OTP Harbor product agenda

This is the maintained product agenda. Security, recovery confidence, local-first behavior, and
license compliance take priority over convenience or distribution reach.

1. **Issuer and logo handling — implemented baseline, continue refinement.** Keep third-party
   artwork outside OTP Harbor distributions. A versioned, logo-free local alias resolver improves
   matching for user-imported icon packs, manifest-free packs are indexed from canonical SVG
   filenames, and users can override automatic matching per account from the installed pack.
2. **Import and export workflow — implemented baseline, continue hardening.** Keep migration from
   Aegis, Google Authenticator, 2FAS, URI lists, and OTP Harbor backups explicit, recoverable, and
   well-tested.
3. **Large-vault navigation — in progress.** Search supports multiple terms. Desktop users can
   create color-coded groups, assign or move accounts, filter from horizontally scrollable group
   cards, and edit or delete groups from a right-click menu without deleting their accounts. The
   default list shows ungrouped accounts, while search also matches group names. Continue with
   favorites and additional organization for vaults containing hundreds of accounts.
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
9. **Official F-Droid Android distribution — planned.** Add reproducible
   Android release builds, F-Droid metadata, deterministic dependency verification, signing and
   update documentation, and an independently auditable publishing workflow. GPL-3.0-only satisfies
   the free-software licensing requirement; dependency, toolchain, reproducibility, metadata, and
   maintainer-review requirements remain implementation gates.

## F-Droid acceptance gate

Do not claim that OTP Harbor is available from the official F-Droid repository until it has been
accepted there. GPLv3 permits commercial reuse and forks when its license, source-disclosure, and
copyleft conditions are followed; F-Droid availability must not be described as preserving the
superseded noncommercial restriction.

Submission requires a clean source build without proprietary dependencies, reproducibility checks,
reviewed metadata and screenshots, an F-Droid-compatible update strategy, documented recovery, and
release validation that remains independent from Google Play and GitHub APK signing.
