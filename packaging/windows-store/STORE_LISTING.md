# Microsoft Store listing copy

OTP Harbor is publicly available as Microsoft Store product `9P31KH5L924P`. Keep this reviewed copy aligned with every future Partner Center submission, and re-check field limits before editing the live listing. Screenshots must use synthetic accounts only.

Store page: `https://apps.microsoft.com/detail/9P31KH5L924P`

Windows Store deep link: `ms-windows-store://pdp/?productid=9P31KH5L924P`

Support URL: `https://github.com/Legends/otp-harbor/issues`

Privacy URL: `https://github.com/Legends/otp-harbor/blob/master/PRIVACY.md`

Website URL: `https://legends.github.io/otp-harbor/`

## Partner Center visual assets

### Upcoming v3 artwork

The current published screenshots below are the older listing set and must not be described as v3. The new localized v3 branded campaign candidates are generated from the canonical marketing assets at `artifacts/marketing/store/windows/promotional/<culture>/` (`en-US`, `de-DE`, `fr-FR`, `es-ES`). Each folder contains ten 1920 × 1080 PNG slides in this order:

1. `02-dark-account-list.png` — Dark theme is the main visual.
2. `03-light-dark-themes.png` — Light/Dark comparison; retained at the user's request.
3. `04-appearance-settings.png`
4. `05-account-actions.png`
5. `06-quick-unlock.png` — Actual Windows Security prompt cutout, no Loading screen.
6. `07-qr-import.png` — Documented demonstration composite; example QR opens the project website.
7. `08-groups-favourites.png` — Designed groups and favourites slide using OTP Harbor's own account-organization onboarding artwork.
8. `09-icon-pack.png` — Designed icon-pack slide using OTP Harbor's own icon-pack onboarding artwork. It points users to the [OTP Harbor Icon Pack Builder on GitHub](https://github.com/Legends/otp-harbor-icon-pack-builder), explains local pack import, and states that users remain responsible for third-party logo license and trademark compliance. No third-party logos are bundled in the slide.
9. `10-backup-restore.png` — Designed backup/restore illustration using OTP Harbor's own account-migration artwork.
10. `11-import-formats.png` — Designed Add accounts illustration and text listing verified import formats, without third-party logos.

Run `scripts/marketing/Generate-MarketingAssets.ps1` to regenerate them. `artifacts/marketing/manifest.json` records source capture and onboarding illustration hashes, crops, app placement, and `storeCampaignOrder`. The Light-only opener `01-light-account-list.png` was removed; the Dark account list leads. These are designed campaign composites like the existing Store gallery, not genuine unedited screenshots. Slides 08–11 are conceptual illustrations, clearly distinguished from the real app captures in 02–07. Slide 11 names supported formats as compatibility information: unencrypted Aegis JSON, unencrypted 2FAS `.2fas`, standard `otpauth://` URI lists in `.txt`, and Google Authenticator QR images; it uses no provider logos. The separate plain screenshot candidates are under `artifacts/marketing/store/windows/screenshots/`. The prompt depicts the user's actual PIN fallback; fingerprint availability depends on the Windows device and is not represented by a fabricated fingerprint screen. Manually review all v3 slides, fictional marks, localized copy, and Partner Center acceptance before replacing the current listing. No v3 listing has been submitted.

In **Store logos / Images for Store display**, upload these files from
`packaging/windows-store/assets` in the matching square slots:

- `store-app-tile-300x300.png` — 1:1 app tile
- `store-logo-150x150.png` — 1:1 150 x 150 Store logo
- `store-logo-71x71.png` — 1:1 71 x 71 Store logo

In the larger **Store logos** slots, upload:

- `store-poster-art-720x1080.png` — 9:16 poster art
- `store-box-art-1080x1080.png` — 1:1 box art

In **Trailers and additional assets / Windows 10 or Windows 11 and Xbox image**, upload:

- `store-super-hero-1920x1080.png` — 16:9 super hero art

Do not upload `assets/source/store-abstract-background.png`; it is only the generated source used
to compose the final assets. Poster and box art are primarily used in game-oriented Store layouts,
but Partner Center accepts them for this app listing and they keep the optional slots polished.

For each localized Store listing, upload its polished 1920 x 1080 campaign set from
`packaging/windows-store/screenshots/<culture>/marketing` in this order:

1. `02-dark-account-list.png`
2. `03-light-dark-themes.png`
3. `04-appearance-settings.png`
4. `05-account-actions.png`
5. `06-quick-unlock.png`
6. `07-qr-import.png`
7. `08-groups-favourites.png`
8. `09-icon-pack.png`
9. `10-backup-restore.png`
10. `11-import-formats.png`

Available culture folders are `en-US`, `de-DE`, `fr-FR`, and `es-ES`.
[`Generate-MarketingAssets.ps1`](../../scripts/marketing/Generate-MarketingAssets.ps1) creates the
campaign from canonical v3 captures and then hash-verifies the committed submission copies through
`Sync-WindowsStoreSubmissionAssets.ps1`. Run it after changing campaign copy or a source capture;
Store artwork remains outside the MSIX and is validated as a static release asset.

These campaign images use the reviewed background, deterministic fictional v3 accounts and
original geometric marks. Slides 02–07 contain genuine captures or documented composites. Slides
08–11 are explicitly conceptual OTP Harbor promotions and must not be represented as literal UI.
The canonical genuine captures remain under `artifacts/marketing/source/desktop`.

## English (en-US)

Product name: `OTP Harbor`

Short description: `Local-first TOTP authenticator with encrypted storage and no cloud account.`

Description:

> OTP Harbor is a GPLv3-licensed open-source TOTP and 2FA authenticator designed to keep your authentication secrets under your control. Accounts are stored in an encrypted local vault and no OTP Harbor cloud account is required.
>
> Add accounts manually or scan an otpauth QR code, search your account list, copy the current code, and configure automatic clipboard clearing and locking. Windows Hello provides convenient quick unlock while your master password remains the recovery method. Encrypted backups support deliberate recovery and migration.
>
> The complete corresponding source and GPLv3 license for each released version are available at https://github.com/Legends/otp-harbor/releases.

Key features:

- Encrypted local TOTP vault
- Windows Hello quick unlock with master-password recovery
- QR-code import and account QR display
- Search, editing, deletion, and timed clipboard clearing
- Idle, minimize, and Windows session locking
- Encrypted backup and restore
- English, German, French, and Spanish interface

Search terms: `TOTP,2FA,authenticator,OTP,offline,security`

## German (de-DE)

Product name: `OTP Harbor`

Short description: `Lokaler TOTP-Authenticator mit verschlüsseltem Speicher ohne Cloudkonto.`

Description:

> OTP Harbor ist ein unter GPLv3 lizenzierter quelloffener TOTP- und 2FA-Authenticator, bei dem Ihre Anmeldegeheimnisse unter Ihrer Kontrolle bleiben. Konten werden in einem verschlüsselten lokalen Tresor gespeichert; ein OTP-Harbor-Cloudkonto ist nicht erforderlich.
>
> Fügen Sie Konten manuell oder per otpauth-QR-Code hinzu, durchsuchen Sie die Kontoliste, kopieren Sie den aktuellen Code und konfigurieren Sie das automatische Leeren der Zwischenablage sowie die Sperrung. Windows Hello ermöglicht schnelles Entsperren, während das Masterpasswort die Wiederherstellungsmethode bleibt. Verschlüsselte Backups unterstützen eine kontrollierte Wiederherstellung und Migration.
>
> Der vollständige korrespondierende Quellcode und die GPLv3-Lizenz jeder veröffentlichten Version sind unter https://github.com/Legends/otp-harbor/releases verfügbar.

Key features:

- Verschlüsselter lokaler TOTP-Tresor
- Schnelles Entsperren mit Windows Hello und Masterpasswort-Wiederherstellung
- QR-Code-Import und Anzeige von Konto-QR-Codes
- Suche, Bearbeitung, Löschen und zeitgesteuertes Leeren der Zwischenablage
- Sperrung bei Leerlauf, Minimierung und Windows-Sitzungssperre
- Verschlüsseltes Backup und Wiederherstellung
- Oberfläche auf Englisch, Deutsch, Französisch und Spanisch

Search terms: `TOTP,2FA,Authentifikator,OTP,offline,Sicherheit`

## French (fr-FR)

Product name: `OTP Harbor`

Short description: `Authentificateur TOTP local avec stockage chiffré, sans compte cloud.`

Description:

> OTP Harbor est un authentificateur TOTP et 2FA open source sous licence GPLv3, conçu pour garder vos secrets d’authentification sous votre contrôle. Les comptes sont conservés dans un coffre local chiffré et aucun compte cloud OTP Harbor n’est requis.
>
> Ajoutez des comptes manuellement ou scannez un code QR otpauth, recherchez un compte, copiez le code actuel et configurez l’effacement automatique du presse-papiers ainsi que le verrouillage. Windows Hello permet un déverrouillage rapide, tandis que le mot de passe principal reste la méthode de récupération. Les sauvegardes chiffrées facilitent une récupération et une migration maîtrisées.
>
> Le code source correspondant complet et la licence GPLv3 de chaque version publiée sont disponibles sur https://github.com/Legends/otp-harbor/releases.

Key features:

- Coffre TOTP local chiffré
- Déverrouillage rapide avec Windows Hello et récupération par mot de passe principal
- Importation par code QR et affichage du code QR d’un compte
- Recherche, modification, suppression et effacement temporisé du presse-papiers
- Verrouillage après inactivité, réduction ou verrouillage de session Windows
- Sauvegarde et restauration chiffrées
- Interface en anglais, allemand, français et espagnol

Search terms: `TOTP,2FA,authentificateur,OTP,hors ligne,sécurité`

## Spanish (es-ES)

Product name: `OTP Harbor`

Short description: `Autenticador TOTP local con almacenamiento cifrado y sin cuenta en la nube.`

Description:

> OTP Harbor es un autenticador TOTP y 2FA de código abierto con licencia GPLv3, diseñado para mantener sus secretos de autenticación bajo su control. Las cuentas se guardan en un almacén local cifrado y no se necesita una cuenta de OTP Harbor en la nube.
>
> Añada cuentas manualmente o escanee un código QR otpauth, busque en la lista, copie el código actual y configure el borrado automático del portapapeles y el bloqueo. Windows Hello permite un desbloqueo rápido, mientras que la contraseña maestra sigue siendo el método de recuperación. Las copias de seguridad cifradas permiten una recuperación y migración controladas.
>
> El código fuente correspondiente completo y la licencia GPLv3 de cada versión publicada están disponibles en https://github.com/Legends/otp-harbor/releases.

Key features:

- Almacén TOTP local cifrado
- Desbloqueo rápido con Windows Hello y recuperación mediante contraseña maestra
- Importación mediante QR y visualización del QR de la cuenta
- Búsqueda, edición, eliminación y borrado temporizado del portapapeles
- Bloqueo por inactividad, minimización y bloqueo de sesión de Windows
- Copia de seguridad y restauración cifradas
- Interfaz en inglés, alemán, francés y español

Search terms: `TOTP,2FA,autenticador,OTP,sin conexión,seguridad`

## Version 2.0.1 release notes

English: `Keeps your position in long account lists after copying a code instead of returning to the top when the confirmation disappears.`

German: `Behält nach dem Kopieren eines Codes die Position in langen Kontolisten bei, anstatt beim Ausblenden der Bestätigung zum Anfang zurückzuspringen.`

French: `Conserve votre position dans les longues listes de comptes après la copie d’un code, au lieu de revenir en haut lorsque la confirmation disparaît.`

Spanish: `Mantiene la posición en listas de cuentas largas después de copiar un código, en lugar de volver al principio cuando desaparece la confirmación.`

### Draft version 3.0.0 release notes

English: `Adds Light and Dark themes, account groups and favourites, optional locally imported icon packs, safer v2 vault migration with rollback snapshots, and clearer conflict review for backups and older vaults.`

German: `Führt helle und dunkle Designs, Kontogruppen und Favoriten, optional lokal importierte Symbolpakete, eine sicherere Migration von v2-Tresoren mit Rollback-Sicherungen sowie eine übersichtlichere Konfliktprüfung für Backups und ältere Tresore ein.`

French: `Ajoute les thèmes clair et sombre, les groupes de comptes et les favoris, des packs d’icônes importés localement en option, une migration plus sûre des coffres v2 avec instantanés de restauration et un examen plus clair des conflits pour les sauvegardes et les anciens coffres.`

Spanish: `Añade temas claro y oscuro, grupos de cuentas y favoritos, paquetes de iconos importados localmente de forma opcional, una migración más segura de bóvedas v2 con instantáneas de reversión y una revisión más clara de conflictos para copias de seguridad y bóvedas anteriores.`
