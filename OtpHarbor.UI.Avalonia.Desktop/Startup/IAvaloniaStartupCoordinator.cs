namespace TOTP.Avalonia.Desktop.Startup;

public interface IAvaloniaStartupCoordinator
{
    Task<AvaloniaStartupOutcome> InitializeAsync(CancellationToken cancellationToken = default);
}

public enum AvaloniaStartupOutcome
{
    ReadyForPasswordSetup,
    ReadyForUnlock,
    ReadyForQuickUnlock,
    ReadyForPasswordFallback,
    ReadyUnlocked,
    PreferencesUnavailable,
    UnexpectedFailure
}
