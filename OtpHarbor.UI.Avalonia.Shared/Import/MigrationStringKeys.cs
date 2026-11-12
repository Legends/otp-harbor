namespace TOTP.Avalonia.Shared.Import;

public static class MigrationStringKeys
{
    public const string Title = "MigrationImportTitle";
    public const string Description = "MigrationImportDescription";
    public const string ChooseSource = "MigrationChooseSource";
    public const string InstructionsTitle = "MigrationInstructionsTitle";
    public const string PlaintextOnly = "MigrationPlaintextOnly";
    public const string QrOnly = "MigrationQrOnly";
    public const string GuideOnly = "MigrationGuideOnly";
    public const string SelectExport = "MigrationSelectExport";
    public const string ChooseFile = "MigrationChooseFile";
    public const string ChooseQr = "MigrationChooseQr";
    public const string ReviewNext = "MigrationReviewNext";
    public const string LocalProcessing = "MigrationLocalProcessing";
    public const string ReviewSummary = "MigrationReviewSummary";
    public const string ReviewEntry = "MigrationReviewEntry";
    public const string ReviewBlocked = "MigrationReviewBlocked";
    public const string RestoreSeparate = "MigrationRestoreSeparate";
    public const string CurrentVault = "MigrationCurrentVault";
    public const string DataAndBackup = "MigrationDataAndBackup";
    public const string InputDuplicates = "MigrationInputDuplicates";
    public static string Source(AuthenticatorImportSource source) => $"MigrationSource{source}";
    public static string Guide(AuthenticatorImportSource source) => $"MigrationGuide{source}";
    public static IReadOnlyList<string> All { get; } =
    [
        Title, Description, ChooseSource, InstructionsTitle, PlaintextOnly, QrOnly, GuideOnly,
        SelectExport, ChooseFile, ChooseQr, ReviewNext, LocalProcessing, ReviewSummary, ReviewEntry,
        ReviewBlocked, RestoreSeparate, CurrentVault, DataAndBackup, InputDuplicates,
        .. Enum.GetValues<AuthenticatorImportSource>().SelectMany(source => new[] { Source(source), Guide(source) })
    ];
}
