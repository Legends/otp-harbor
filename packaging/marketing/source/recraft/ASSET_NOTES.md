# Recraft biometric artwork

The SVG files in this directory are the unmodified Recraft exports supplied for OTP Harbor's
biometric unlock and marketing presentation. Keep these masters intact so their embedded C2PA
provenance remains available.

Runtime assets are optimized derivatives under
`TOTP.UI.Avalonia.Shared/Assets/Biometric`. They remove generated placeholder text, provide a
light-theme treatment, and keep the interactive fingerprint separate from the static background
so Android does not render hundreds of SVG paths during unlock interactions.
