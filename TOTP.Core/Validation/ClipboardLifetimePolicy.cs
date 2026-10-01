namespace TOTP.Core.Validation;

public static class ClipboardLifetimePolicy
{
    public static IReadOnlyList<int> AllowedSeconds { get; } = [5, 10, 15, 20, 30, 60];

    public static int NormalizeSeconds(int seconds) => AllowedSeconds
        .OrderBy(value => Math.Abs((long)value - seconds))
        .ThenBy(value => value)
        .First();

    public static bool IsAllowed(int seconds) => AllowedSeconds.Contains(seconds);
}
