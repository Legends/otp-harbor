namespace TOTP.Avalonia.Desktop.Startup;

public interface IAvaloniaStartupCoordinator
{
    Task<AvaloniaStartupOutcome> InitializeAsync(CancellationToken cancellationToken = default);
}

public enum AvaloniaStartupOutcome
{
    ReadyForPasswordSetup,
    ReadyForUnlock,
    ReadyForPasswordFallback,
    ReadyUnlocked,
    PreferencesUnavailable,
    UnexpectedFailure
}
