# Synthetic authenticator import kit

Generate supported-schema import files and QR PNGs, then validate them with the
production `ExportService`, `AccountImportService` and `QrAccountImportService`.
The account manager used here is an isolated in-memory implementation. The tool
does not open an installed app, personal export, vault or application-data directory.

```powershell
dotnet run --project scripts/testing/fixtures/authenticator-imports -- artifacts/shared/testing/my-import-kit
```

The output directory must be empty. The kit contains per-provider instructions,
parameter tables, positive/duplicate/conflict/invalid cases, supported historical
schemas, Ente deletion cases, native encrypted/tampered/legacy backup cases, and a
SHA-256 manifest. Foreign encrypted-marker cases are rejection fixtures, not
decryptable vendor exports. Microsoft/Authy folders contain migration guidance and
standard TOTP setup examples rather than native vendor-export claims.

To generate and copy to an authorized Android emulator/device:

```powershell
./scripts/testing/android/Copy-AuthenticatorImportTestKitToAndroid.ps1 -Device emulator-5554 -AdbPath D:/android-platform-tools/adb.exe
```

Pass `-KitDirectory` to copy an already generated kit. The script checks the local
manifest, preserves existing Downloads kits by choosing a fresh destination, and
verifies every copied file using Android SHA-256. All files are submitted for
Android document/media indexing. Begin with `START-HERE.txt`, then each provider's
`README.txt`. In Aegis, use Settings > Import & Export > Import from file > Aegis,
then browse Downloads > OTP-Harbor-Import-Tests > Aegis. Import from app and backup
directory selection are separate workflows.

Fixtures are synthetic and based on the schemas referenced in
[the format audit](../../../docs/security/AUTHENTICATOR_EXPORT_FORMAT_AUDIT.md) and
[the Ente/2FAS comparison](../../../docs/security/AUTHENTICATOR_IMPORT_PARITY.md).
They are not a collection of app-generated exports from every vendor/version.
