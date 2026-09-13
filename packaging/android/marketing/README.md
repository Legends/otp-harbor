# Android website marketing images

This folder holds the authentic Android campaign captures and the reproducible composites used by
`https://legends.github.io/otp-harbor/android/`.

## Synthetic capture vault

Marketing captures must use synthetic data only. The reviewed account roster is:

| Issuer | Account name |
| --- | --- |
| Amazon | `Mason` |
| Apple | `Olivia` |
| Discord | `Lucas` |
| Dropbox | `Ava` |
| GitHub | `Ethan` |
| Google | `Sophia` |

Use only dedicated synthetic Base32 test values. Never capture a real account, seed, backup,
password, notification, device identifier, or personal file name. The visible rotating codes and
the Dropbox transfer QR in the reviewed captures are generated exclusively from the synthetic
fixture seed. Treat even this synthetic QR as sensitive-looking material: do not reuse it outside
the documented campaign or mistake it for a live account.

## Planned campaign set

The English website set uses five 1920×1080 composites:

1. `01-encrypted-local-vault.png` — the account list with the reviewed synthetic roster.
2. `02-camera-and-google-qr.png` — camera scanning and Google Authenticator transfer QR import.
3. `03-biometric-quick-unlock.png` — OTP Harbor unlock plus its genuine biometric-security state.
4. `04-swipe-manage-show-qr.png` — swipe actions and a per-account synthetic QR preview.
5. `05-backup-and-languages.png` — portable encrypted backups and the four supported languages.

The generated backdrop contains no product UI. Authentic app/system captures are composited later so
the campaign never invents a security control or advertises an unavailable feature. Google
Authenticator's documented Android transfer flow scans the export QR with the camera. The current
OTP Harbor Android build does not import an existing QR image file.

## Isolated capture build

Never relax screen-capture protection in a release package. Build the separate debug application ID
with the explicit capture flag instead:

```powershell
dotnet build TOTP.UI.Avalonia.Android/TOTP.UI.Avalonia.Android.csproj `
  -c Debug -p:EnableMarketingCapture=true
```

This defines `OTP_HARBOR_MARKETING_CAPTURE` only for Debug. The resulting
`io.github.legends.otpharbor.debug` package keeps its own app data and can be populated solely with
the reviewed synthetic roster. Normal Debug and every Release build retain Android's secure-window
protection for the account list.

## Source layout

- `source/android-marketing-background.png`: generated reusable background.
- `source/captures/`: reviewed raw Android captures from the isolated synthetic-data package.
- `en-US/`: five final 1920×1080 website composites.
- `../google-play/en-US/`: six 1080×1920 phone screenshots, the 1024×500 feature graphic, and the
  512×512 high-resolution app icon.

Run `scripts/assets/New-AndroidMarketingImages.ps1` to regenerate both final sets. The compositor
keeps all app and Android-system UI pixel-authentic; only crop, scale, rounded clipping, framing,
background, and campaign copy are added.

The public site should reference only final files under `en-US/`, never raw device captures.
