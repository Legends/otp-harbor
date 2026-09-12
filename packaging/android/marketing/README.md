# Android website marketing images

This folder holds the authentic Android campaign captures and the reproducible composites used by
`https://legends.github.io/otp-harbor/android/`.

## Synthetic capture vault

Marketing captures must use synthetic data only. The reviewed account roster is:

| Issuer | Account name |
| --- | --- |
| Amazon | `shopper@example.invalid` |
| Cloudflare | `admin@example.invalid` |
| Discord | `community@example.invalid` |
| Dropbox | `files@example.invalid` |
| GitHub | `octocat@example.invalid` |
| Google | `maya@example.invalid` |
| Microsoft | `alex.wilber@example.invalid` |
| Proton | `privacy@example.invalid` |

Use only published synthetic Base32 test values. Never capture a real account, seed, QR code,
one-time password, backup, password, notification, device identifier, or personal file name. The
visible rotating codes in reviewed captures are generated exclusively from the synthetic seeds.

## Planned campaign set

The English website set uses four 1920×1080 composites:

1. `01-encrypted-local-vault.png` — the account list with the reviewed synthetic roster.
2. `02-camera-and-google-qr.png` — camera scanning and Google Authenticator transfer QR import.
3. `03-biometric-quick-unlock.png` — OTP Harbor unlock plus its genuine biometric-security state.
4. `04-swipe-manage-show-qr.png` — swipe actions and a per-account synthetic QR preview.

The generated backdrop contains no product UI. Authentic app/system captures are composited later so
the campaign never invents a security control or advertises an unavailable feature. Google
Authenticator's documented Android transfer flow scans the export QR with the camera; neither that
workflow nor the current OTP Harbor Android build imports an existing QR image file.

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
- `en-US/`: final website composites (generated only after capture review).

The public site should reference only final files under `en-US/`, never raw device captures.
