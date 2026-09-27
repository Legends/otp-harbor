# OTP Harbor product agenda

This is the maintained product agenda. Security, recovery confidence, local-first behavior, and
license compliance take priority over convenience or distribution reach.

1. **Issuer and logo handling — implemented baseline, continue refinement.** Keep third-party
   artwork outside OTP Harbor distributions. A versioned, logo-free local alias resolver improves
   matching for user-imported icon packs, manifest-free packs are indexed from canonical SVG
   filenames, and users can override automatic matching per account from the installed pack.
   Optional local icon-pack management lives under Appearance rather than account import/export.
2. **Import and export workflow — implemented baseline, continue hardening.** Keep migration from
   Aegis, Google Authenticator, 2FAS, URI lists, and OTP Harbor backups explicit, recoverable, and
   well-tested.
3. **Large-vault navigation — in progress.** Search supports multiple terms. Desktop users can
   create color-coded groups, assign or move accounts, filter from horizontally scrollable group
   cards, and edit or delete groups from a right-click menu without deleting their accounts. The
   default list shows ungrouped accounts, while search also matches group names. Favorites are
   available on desktop and Android; on desktop the built-in Favorites group is first in the strip
   and can be edited to change its color and account membership without exposing a destructive
   group-delete action. Continue with additional organization for vaults containing hundreds of
   accounts.
4. **Keyboard shortcuts — implemented baseline.** Preserve fast search, code copying, accessible
   focus order, and near mouse-free desktop operation.
5. **First-run and onboarding — implemented baseline, continue refinement.** Explain vault
   creation, encryption, backups, and recovery without overwhelming new users. After vault setup,
   the empty account view now presents a focused first-account card with a direct add action while
   retaining scan, import, and encrypted-restore guidance.
6. **UI consistency — ongoing.** Continue reviewing spacing, dialogs, context menus, focus,
   confirmations, accessibility, and resizing behavior. Desktop transient messages use one
   bottom-center overlay treatment outside layout flow, with theme-contrasting surfaces and longer
   warning/error visibility.
7. **Backup UX — implemented baseline, continue refinement.** Backup locations, encryption status,
   recovery requirements, and dedicated restore workflows are now explicit in the desktop settings
   flow; continue refining platform-provider guidance and physical acceptance.
8. **Cross-platform consistency — ongoing.** Keep Windows, Linux, Android, and eventually macOS
   behavior and appearance coherent; macOS implementation work is currently deferred.
9. **Official F-Droid Android distribution — in progress.** The Android dependency graph and SDK are
   pinned, CI enforces locked restores and excludes known proprietary SDK families, localized
   Fastlane metadata is versioned, and an APK payload comparator has exposed remaining .NET Android
   nondeterminism. Continue with toolchain acceptance, reproducibility, signing/update policy, and an
   independently auditable publishing workflow. GPL-3.0-only satisfies the free-software licensing
   requirement; toolchain, reproducibility, and maintainer-review requirements remain gates.

## F-Droid acceptance gate

Do not claim that OTP Harbor is available from the official F-Droid repository until it has been
accepted there. GPLv3 permits commercial reuse and forks when its license, source-disclosure, and
copyleft conditions are followed; F-Droid availability must not be described as preserving the
superseded noncommercial restriction.

Submission requires a clean source build without proprietary dependencies, reproducibility checks,
reviewed metadata and screenshots, an F-Droid-compatible update strategy, documented recovery, and
release validation that remains independent from Google Play and GitHub APK signing.
