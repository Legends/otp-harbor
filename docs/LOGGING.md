# Diagnostic logging

Desktop and Android use the same Serilog level switch and secret-redacting rolling-file formatter.
The Microsoft logging bridge accepts Trace and above, allowing the selected app level to perform
the filtering. Levels persist in preferences and take effect at runtime.
Changing a level in Settings takes effect after its successful automatic save; no restart is
required. A desktop command-line logging override continues to control that process. The selected
preference is still saved for future launches without an override.

| App level | Microsoft logging | Typical events |
| --- | --- | --- |
| Verbose | Trace | Import validation milestones, loaded account counts |
| Debug | Debug | Workflow start, preferences loaded, lifecycle transitions, cancelled imports |
| Information | Information | Startup completion, account mutations, import outcomes, export/backup completion, unlock/lock, settings saved, diagnostic draft opened |
| Warning | Warning | Invalid/unsupported import, rejected unlock, unavailable backup destination |
| Error | Error | Failed startup, account persistence, unexpected workflow or OS-integration failure |
| Fatal | Critical | Existing unrecoverable application/OS exception boundaries |

The selected level is a minimum: Information includes Warning, Error and Fatal, while Verbose
includes all six. Warning/Error/Fatal do not record routine successful actions, and no synthetic
failures are generated just to populate those levels. Production fatal handlers are not invoked by
the level-filter tests; synthetic events use an isolated test logger.

Normal operations now emit events; previously a successful Google QR import could finish without
any event, leaving Android without a log file even at Verbose. The desktop bridge also previously
filtered below Information before the runtime switch could see those events.

New events contain fixed English messages and bounded operational values such as status enums,
counts, timings, method names, or exception type. They do not serialize account objects, issuer or
account names, IDs, QR/URI payloads, OTP seeds/codes, passwords, imported text, result-error contents,
or cryptographic material. Cancellation remains cancellation, not an error.

Files roll daily as `appYYYYMMDD.log`. Desktop exposes its location through **Open log folder**.
Android stores logs in the private application cache's `Logs` directory. Clearing cache, app data,
or OS cache reclamation can remove old Android logs; future logged events create a new file.
The existing asynchronous sink keeps normal file writes off the UI thread. Android's unrecoverable
exception boundary now flushes it before termination; a hard OS process kill cannot guarantee a
final flush.

To verify on a device: select Verbose, restart and unlock, import the synthetic ten-account Google
QR fixture, then choose **Email diagnostic logs** in Misc. Confirm the draft has a log with startup
and import count/status events. Review it before sending. Repeat at Information; normal successes
should remain visible while Debug/Verbose detail is filtered out.

**Report on GitHub**, also in Misc, opens the bug-report form with the installed application
version and OS prefilled. It works even when no diagnostic log exists. GitHub issues are public;
logs and private diagnostic fields are never automatically attached. Review the form and use
synthetic reproduction data before submitting. Security issues belong in the private process in
`SECURITY.md`. The repository's platform field is a text input so GitHub's documented custom-field
URL prefilling applies; deploy that template change on the default branch along with this feature.
