# Privacy policy

OTP Harbor is a local-first desktop and Android application. It does not contain telemetry, analytics, advertising, or a project-operated cloud service. Account labels, OTP seeds, generated codes, passwords, vault contents, exports, application logs, and camera frames are not automatically sent to the project maintainers.

## Network activity

GitHub direct-download packages contact the configured GitHub or GitHub Pages endpoint after the vault is unlocked to retrieve the signed update feed. An update package is downloaded only after the user requests the download. These requests disclose ordinary connection metadata, such as the user's IP address, to GitHub and its delivery providers. They do not include vault data or a project-specific user identifier. Externally managed package builds disable this application-owned check. GitHub describes its handling of service data in the [GitHub Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).

Packages managed by an operating-system package manager or app store use that distributor's update mechanism and privacy terms instead of the application's direct-update service.

The application accesses the camera only during a user-initiated QR scan. Camera frames are processed on the device and are not transmitted. Platform quick-unlock services, the clipboard, application-data storage, and log files are also used locally through operating-system interfaces.

If a user explicitly follows a link, downloads a release, reports an issue, or contacts a maintainer, the selected external service processes the information the user chooses to provide under that service's privacy policy. Never include OTP seeds, passwords, recovery data, vault files, or other secrets in a support request.

Choosing to email diagnostic logs opens an editable email/share draft addressed to `otpharbor.support@proton.me`. The draft contains a system summary collected at that time: app version/build/configuration and distribution mode, OS version, CPU architecture, .NET/Avalonia versions, logical window size and display scaling where available, theme, app language, log level, and a UTC collection timestamp. Android also includes its API level, manufacturer, and model; desktop reports do not query computer manufacturer/model. No account/vault data, hardware serial numbers, persistent device identifiers, usernames, computer names, network addresses, screenshots, or clipboard contents are added to this summary. Existing logs may contain system and file-path details despite secret redaction. Users can review or remove the summary and attachment before sending through their chosen email application. Attachments depend on client support; manual attachment may be required. No message is sent automatically.

Choosing **Report on GitHub** opens the public bug-report form in the system browser. The app version and OS description are sent to GitHub as URL query parameters when the page opens, even before the user submits the form. No logs, account/vault data, device model or identifiers, paths, or other diagnostic-summary fields are attached or added to this URL. The user can edit the prefilled fields and decides whether to submit a public issue; a GitHub account and internet access may be required. Security vulnerabilities should be reported privately through [SECURITY.md](SECURITY.md).

## Data retention

The project maintainers receive no application telemetry and therefore retain no application-usage records. Local vaults, settings, backups, exports, and logs remain under the user's control and are removed using normal filesystem or application-data deletion procedures.

Privacy or security concerns may be reported using the private process in [SECURITY.md](SECURITY.md).
