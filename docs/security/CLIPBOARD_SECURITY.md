# Clipboard security

## Scope

Clipboard policy is separated from desktop-framework access. `AsyncClipboardService` owns plain-copy and timed-clear orchestration through the portable `IAsyncClipboardService`; Avalonia platform adapters provide clipboard access behind `IAsyncPlatformClipboard`.

## Security behavior

- Clipboard operations return explicit result codes for unavailable, write-failed, and clear-failed states.
- Auto-clear is offered only when the adapter advertises both text-write and conditional-clear capabilities.
- Plain copying requires only text-write capability. Disabling auto-clear never disables the core copy action.
- A successful write returns an opaque change token. The policy retains only that token and the deadline, never a second copy of the OTP text.
- At timeout, the adapter clears only when the platform clipboard change token still matches the original write. If the user or another application replaced the clipboard, clearing is skipped.
- A clear failure is logged without clipboard content and is not retried after the configured deadline.
- A later unscheduled copy cancels the previous clear schedule.
- Logs record operation type, timing, and error code only. Clipboard contents and change tokens are not logged.
- UI success state is shown only after a successful copy; failures use localized, recoverable messages.
- Android account rows do not copy an OTP while an account QR code is visible. Dismissing the QR restores normal copying. The QR payload remains in the QR display workflow and is not written to the clipboard by an account tap.
- Android clears displayed codes when the app backgrounds for the file picker. Canceling the editor after returning regenerates current and next codes only when the account list is visible, foregrounded, and unlocked. It does not keep the prior code alive across that transition.

## Platform capability policy

Platform adapters must provide a reliable native change token or equivalent conditional-clear primitive before advertising `ConditionalClear`. If safe replacement detection is unavailable, the application performs a normal copy and displays a localized warning that automatic clearing is unavailable. It must never clear clipboard contents unconditionally.

Platform adapters are boundary components and must translate native clipboard/dispatcher failures into result errors without including clipboard content in exception messages or logs.

## Threat impact and limitations

- This reduces the lifetime of OTP material in application-managed memory and avoids deleting unrelated clipboard content.
- The operating-system clipboard is shared state. Other applications running as the same user may read an OTP while it is present.
- Timed clearing is best effort and cannot revoke content already observed or synchronized by clipboard-history/cloud features.
- On Android 13 and later the clipboard adapter flags OTP text as sensitive to hide its system preview. This flag does not block clipboard reads by a permitted system or cross-device clipboard service.
- When auto-clear is disabled or unsupported, the copied OTP may remain in clipboard history after it expires; the UI reports the unsupported case explicitly.
- The Windows sequence number detects replacement but is not a confidentiality control.

## Compatibility

- The application default clear duration is 15 seconds.
- The Avalonia account workflow uses the configured clear duration.
- Disabling automatic clear still performs a normal clipboard write and cancels any older pending clear.
