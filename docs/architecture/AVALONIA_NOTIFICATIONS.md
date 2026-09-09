# Avalonia notification policy

Avalonia action feedback uses two shared building blocks:

- `NotificationState` owns text, severity, visibility, replacement, cancellation, and transient lifetime.
- `NotificationBanner` renders that state consistently and supplies severity styling and accessibility live-region behavior.

Notifications remain owned by the narrowest relevant presentation context, but each window renders them through one non-interactive overlay at the bottom of the window. The Settings window relays action feedback from every tab into its single overlay, so messages never affect section layout and remain visible when the user changes tabs.

Window-overlay notifications are transient and use the shared 1500 ms default. A newly shown notification replaces the current one and restarts that lifetime, including when the text is unchanged. Persistent failures or instructions that require acknowledgement belong in an owned dialog or inline recovery surface, not in a toast.

Inline validation and workflow status are not general notifications. Keep password, account-field, scanner, and generated-code validation beside the affected control or workflow so the user can identify what requires attention.
