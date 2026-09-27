using TOTP.Core.Services.Interfaces;

namespace TOTP.Avalonia.Desktop.Presentation;

public static class TransientNotificationDefaults
{
    public static TimeSpan Duration { get; } = TimeSpan.FromSeconds(4);
    public static TimeSpan ImportantDuration { get; } = TimeSpan.FromSeconds(6);
    public static TimeSpan CopyConfirmationDuration { get; } = TimeSpan.FromMilliseconds(1500);

    public static TimeSpan ForSeverity(NotificationSeverity severity) =>
        severity is NotificationSeverity.Warning
            or NotificationSeverity.Error
                ? ImportantDuration
                : Duration;
}
