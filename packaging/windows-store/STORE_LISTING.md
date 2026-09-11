# Microsoft Store listing copy

OTP Harbor is publicly available as Microsoft Store product `9P31KH5L924P`. Keep this reviewed copy aligned with every future Partner Center submission, and re-check field limits before editing the live listing. Screenshots must use synthetic accounts only.

Store page: `https://apps.microsoft.com/detail/9P31KH5L924P`

Windows Store deep link: `ms-windows-store://pdp/?productid=9P31KH5L924P`

Support URL: `https://github.com/Legends/otp-harbor/issues`

Privacy URL: `https://github.com/Legends/otp-harbor/blob/master/PRIVACY.md`

Website URL: `https://legends.github.io/otp-harbor/`

## Partner Center visual assets

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

1. `01-local-vault.png`
2. `02-search-and-copy.png`
3. `03-add-and-import.png`
4. `04-windows-hello.png`
5. `05-import-export.png`

Available culture folders are `en-US`, `de-DE`, `fr-FR`, and `es-ES`. The generator
[`New-WindowsStoreMarketingScreenshots.ps1`](../../scripts/assets/New-WindowsStoreMarketingScreenshots.ps1)
keeps the campaign layout and authentic product captures identical while rendering reviewed copy
for each locale. Run it after changing any campaign text or source capture; Store artwork is kept
outside the MSIX and is validated as a static release asset.

These marketing screenshots combine the reviewed campaign background with authentic app captures.
They contain only deterministic synthetic accounts. Their issuer labels are `Amazon`, `Cloudflare`,
`Discord`, `Dropbox`, `GitHub`, `Google`, `Microsoft`, and `Proton`; account names use the reserved
`example.invalid` domain. The captures under `screenshots/en-US` remain the authentic source set.

Suggested German captions, in upload order:

1. `Alle TOTP-Konten übersichtlich in einem lokal verschlüsselten Tresor.`
2. `Konten sofort finden und den aktuellen Code mit einem Klick kopieren.`
3. `TOTP-Konten manuell oder per QR-Code hinzufügen.`
4. `Schnell mit Windows Hello entsperren; das Masterpasswort bleibt die Wiederherstellungsmethode.`
5. `Verschlüsselte Backups importieren und exportieren, Google-Authenticator-QR-Exporte übernehmen oder einzelne Konten per Kamera scannen.`

## English (en-US)

Product name: `OTP Harbor`

Short description: `Local-first TOTP authenticator with encrypted storage and no cloud account.`

Description:

> OTP Harbor is an open-source TOTP and 2FA authenticator designed to keep your authentication secrets under your control. Accounts are stored in an encrypted local vault and no OTP Harbor cloud account is required.
>
> Add accounts manually or scan an otpauth QR code, search your account list, copy the current code, and configure automatic clipboard clearing and locking. Windows Hello provides convenient quick unlock while your master password remains the recovery method. Encrypted backups support deliberate recovery and migration.

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

> OTP Harbor ist ein quelloffener TOTP- und 2FA-Authenticator, bei dem Ihre Anmeldegeheimnisse unter Ihrer Kontrolle bleiben. Konten werden in einem verschlüsselten lokalen Tresor gespeichert; ein OTP-Harbor-Cloudkonto ist nicht erforderlich.
>
> Fügen Sie Konten manuell oder per otpauth-QR-Code hinzu, durchsuchen Sie die Kontoliste, kopieren Sie den aktuellen Code und konfigurieren Sie das automatische Leeren der Zwischenablage sowie die Sperrung. Windows Hello ermöglicht schnelles Entsperren, während das Masterpasswort die Wiederherstellungsmethode bleibt. Verschlüsselte Backups unterstützen eine kontrollierte Wiederherstellung und Migration.

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

> OTP Harbor est un authentificateur TOTP et 2FA open source conçu pour garder vos secrets d’authentification sous votre contrôle. Les comptes sont conservés dans un coffre local chiffré et aucun compte cloud OTP Harbor n’est requis.
>
> Ajoutez des comptes manuellement ou scannez un code QR otpauth, recherchez un compte, copiez le code actuel et configurez l’effacement automatique du presse-papiers ainsi que le verrouillage. Windows Hello permet un déverrouillage rapide, tandis que le mot de passe principal reste la méthode de récupération. Les sauvegardes chiffrées facilitent une récupération et une migration maîtrisées.

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

> OTP Harbor es un autenticador TOTP y 2FA de código abierto diseñado para mantener sus secretos de autenticación bajo su control. Las cuentas se guardan en un almacén local cifrado y no se necesita una cuenta de OTP Harbor en la nube.
>
> Añada cuentas manualmente o escanee un código QR otpauth, busque en la lista, copie el código actual y configure el borrado automático del portapapeles y el bloqueo. Windows Hello permite un desbloqueo rápido, mientras que la contraseña maestra sigue siendo el método de recuperación. Las copias de seguridad cifradas permiten una recuperación y migración controladas.

Key features:

- Almacén TOTP local cifrado
- Desbloqueo rápido con Windows Hello y recuperación mediante contraseña maestra
- Importación mediante QR y visualización del QR de la cuenta
- Búsqueda, edición, eliminación y borrado temporizado del portapapeles
- Bloqueo por inactividad, minimización y bloqueo de sesión de Windows
- Copia de seguridad y restauración cifradas
- Interfaz en inglés, alemán, francés y español

Search terms: `TOTP,2FA,autenticador,OTP,sin conexión,seguridad`

## Version 2.0.0 release notes

English: `First Microsoft Store release of OTP Harbor with encrypted local TOTP storage, Windows Hello quick unlock, QR workflows, locking controls, encrypted backup/restore, and four interface languages.`

German: `Erste Microsoft-Store-Version von OTP Harbor mit verschlüsseltem lokalem TOTP-Speicher, schnellem Entsperren über Windows Hello, QR-Funktionen, Sperroptionen, verschlüsseltem Backup/Wiederherstellung und vier Oberflächensprachen.`

French: `Première version Microsoft Store d’OTP Harbor avec stockage TOTP local chiffré, déverrouillage rapide Windows Hello, fonctions QR, options de verrouillage, sauvegarde/restauration chiffrée et quatre langues d’interface.`

Spanish: `Primera versión de OTP Harbor en Microsoft Store con almacenamiento TOTP local cifrado, desbloqueo rápido con Windows Hello, funciones QR, controles de bloqueo, copia/restauración cifrada y cuatro idiomas de interfaz.`
