# Third-party notices

OTP Harbor uses third-party packages under their respective licenses. Those licenses remain separate from OTP Harbor's GPLv3 license and may grant different rights or obligations. Exact versions are declared in the project files and resolved by `dotnet restore`; dependency and vulnerability review runs in CI.

## Runtime dependencies

- Avalonia and Avalonia Themes Fluent — MIT
- FluentResults — MIT
- Microsoft.Extensions libraries — MIT
- Microsoft.Win32.SystemEvents — MIT
- NSec.Cryptography — MIT
- Otp.NET — MIT
- QRCoder — MIT
- OpenCvSharp and selected native runtimes — Apache-2.0
- Serilog and its configured enrichers/sinks — Apache-2.0
- ZXing.Net — Apache-2.0

## Test dependencies

- xUnit and runner packages — Apache-2.0
- Microsoft.NET.Test.Sdk — MIT
- Moq — BSD-3-Clause
- Avalonia.Headless.XUnit — MIT

## Assets

The application icon and locale flags are project-owned or locally rendered from public-domain flag geometry. Provenance and reviewed hashes are recorded in [docs/assets/ASSET_PROVENANCE.md](docs/assets/ASSET_PROVENANCE.md).

OTP Harbor distributions do not bundle third-party service-logo artwork. Users may optionally create a provider-neutral `.otphicons` file with the separate [OTP Harbor Icon Pack Builder](https://github.com/Legends/otp-harbor-icon-pack-builder) and import it into their local application data. Available upstream license, attribution, and provenance records are preserved in the generated pack; see [docs/assets/BRAND_ICONS.md](docs/assets/BRAND_ICONS.md). Project-level licenses do not necessarily grant rights to individual brand marks. Users are responsible for complying with each asset's license, trademark rights, and usage terms. Displayed trademarks remain the property of their respective owners, and their appearance does not imply sponsorship, affiliation, or endorsement.

This file is an inventory aid, not legal advice. Review upstream license texts when adding or upgrading a dependency.
