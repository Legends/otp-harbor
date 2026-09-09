# UI interaction boundaries

Portable workflows describe user intent without depending on Avalonia types:

- `NotificationSeverity` is the portable severity vocabulary shared by banners and dialog requests.
- Presentation view models own `NotificationState`; they do not call a global notification service.
- Desktop adapters implement `IAvaloniaDialogService`, `IAvaloniaFilePicker`, and the owned QR/scanner dialog contracts.
- Password prompts, QR previews, and scanner workflows do not expose native window types outside the desktop project.

The Avalonia desktop project owns dialogs, window ownership, native file pickers, bitmap conversion, and UI-thread dispatch. QR data crosses portable boundaries only in owned encoded buffers; each temporary buffer and decoded bitmap is cleared or disposed when replaced, hidden, closed, locked, or disposed.

## Notification policy

`NotificationState` is the presentation state for recoverable information, success, warning, and error messages. `NotificationBanner` is their common visual and accessibility surface.

Messages remain owned by the smallest useful presentation context:

- inline validation stays beside the affected field or workflow;
- transient action feedback is relayed to the single bottom overlay for its window;
- instructions and failures requiring acknowledgement stay in an owned dialog or inline recovery surface.

The Settings window aggregates notifications from all tabs without moving their business logic into code-behind. Overlay messages replace one another, dismiss after the shared 1500 ms default, and are cleared when the window opens or closes. View models select localized, presentation-safe text and never expose exception messages or secret-bearing values.
