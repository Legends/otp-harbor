namespace TOTP.Core.Platform;

public sealed record ApplicationActivationRequest(
    int Version,
    ApplicationActivationKind Kind,
    string? Payload = null)
{
    public const int CurrentVersion = 1;

    public static ApplicationActivationRequest ActivateMainWindow() =>
        new(CurrentVersion, ApplicationActivationKind.ActivateMainWindow);

    public static ApplicationActivationRequest DebugBulkAddSyntheticAccounts() =>
        new(CurrentVersion, ApplicationActivationKind.DebugBulkAddSyntheticAccounts);

    public static ApplicationActivationRequest DebugBulkDeleteSyntheticAccounts() =>
        new(CurrentVersion, ApplicationActivationKind.DebugBulkDeleteSyntheticAccounts);

    public static ApplicationActivationRequest DebugImportAccounts(string filePath) =>
        new(CurrentVersion, ApplicationActivationKind.DebugImportAccounts, filePath);

    public static ApplicationActivationRequest DebugDeleteAllAccounts() =>
        new(CurrentVersion, ApplicationActivationKind.DebugDeleteAllAccounts);

    public bool IsSupported => Version == CurrentVersion && Enum.IsDefined(Kind);
}
