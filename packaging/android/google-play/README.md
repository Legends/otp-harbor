# Google Play listing artwork

The English Google Play artwork is generated from reviewed, synthetic-data Android captures by
`scripts/assets/New-AndroidMarketingImages.ps1`.

Upload the files from `en-US` in Play Console under **Grow users > Store presence > Main store
listing > Graphics**:

| Play Console field | File(s) |
| --- | --- |
| App icon | `app-icon-512x512.png` |
| Feature graphic | `feature-graphic-1024x500.png` |
| Phone screenshots | `01-...` through `06-...-1080x1920.png`, in numeric order |

The six phone images are 24-bit, non-transparent PNGs at 1080×1920. The feature graphic is a
24-bit, non-transparent PNG at 1024×500. The app icon is a 32-bit PNG with alpha at 512×512 and is
kept below 1,024 KB. These dimensions and formats follow Google's current preview-asset guidance:
https://support.google.com/googleplay/android-developer/answer/9866151

Suggested English alt text, each below 140 characters:

1. OTP Harbor encrypted account vault with synthetic Amazon, Apple, Discord, Dropbox, GitHub, and Google accounts.
2. OTP Harbor swipe actions for editing and deliberately revealing a synthetic account transfer QR.
3. OTP Harbor camera scan and Google Authenticator transfer import workflows.
4. OTP Harbor unlock choices: strong biometrics, Android screen lock, and master password.
5. OTP Harbor password-protected backup import and export controls.
6. OTP Harbor language selection for English, German, French, and Spanish.

The source video is retained only as future website material. Google Play accepts a YouTube URL,
not an uploaded MP4, for the optional preview-video field.
