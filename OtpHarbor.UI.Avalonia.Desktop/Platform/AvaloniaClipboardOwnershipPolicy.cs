namespace TOTP.Avalonia.Desktop.Platform;

public static class AvaloniaClipboardOwnershipPolicy
{
    public static bool ForCurrentProcess() => IsSupported(
        OperatingSystem.IsWindows(),
        OperatingSystem.IsMacOS(),
        OperatingSystem.IsLinux(),
        Environment.GetEnvironmentVariable("DISPLAY"));

    public static bool IsSupported(
        bool isWindows,
        bool isMacOS,
        bool isLinux,
        string? x11Display) =>
        isWindows
        || isMacOS
        || (isLinux && !string.IsNullOrWhiteSpace(x11Display));
}
