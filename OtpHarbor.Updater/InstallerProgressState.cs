namespace TOTP.Updater;

internal sealed class InstallerProgressState
{
    public required string Title { get; init; }
    public required string Status { get; init; }
    public required string Detail { get; init; }
    public required string ProgressText { get; init; }
    public required bool IsIndeterminate { get; init; }
    public required int ProgressValue { get; init; }
    public required bool IsCloseEnabled { get; init; }
}
