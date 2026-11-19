namespace TOTP.Avalonia.Shared.Import;

public static class MigrationStringKeys
{
    public const string Title = global::TOTP.Localization.CommonStringKeys.MigrationImportTitle;
    public const string Description = global::TOTP.Localization.CommonStringKeys.MigrationImportDescription;
    public const string ChooseSource = global::TOTP.Localization.CommonStringKeys.MigrationChooseSource;
    public const string InstructionsTitle = global::TOTP.Localization.CommonStringKeys.MigrationInstructionsTitle;
    public const string PlaintextOnly = global::TOTP.Localization.CommonStringKeys.MigrationPlaintextOnly;
    public const string QrOnly = global::TOTP.Localization.CommonStringKeys.MigrationQrOnly;
    public const string GuideOnly = global::TOTP.Localization.CommonStringKeys.MigrationGuideOnly;
    public const string SelectExport = global::TOTP.Localization.CommonStringKeys.MigrationSelectExport;
    public const string ChooseFile = global::TOTP.Localization.CommonStringKeys.MigrationChooseFile;
    public const string ChooseQr = global::TOTP.Localization.CommonStringKeys.MigrationChooseQr;
    public const string ReviewNext = global::TOTP.Localization.CommonStringKeys.MigrationReviewNext;
    public const string LocalProcessing = global::TOTP.Localization.CommonStringKeys.MigrationLocalProcessing;
    public const string ReviewSummary = global::TOTP.Localization.CommonStringKeys.MigrationReviewSummary;
    public const string ReviewEntry = global::TOTP.Localization.CommonStringKeys.MigrationReviewEntry;
    public const string ReviewBlocked = global::TOTP.Localization.CommonStringKeys.MigrationReviewBlocked;
    public const string RestoreSeparate = global::TOTP.Localization.CommonStringKeys.MigrationRestoreSeparate;
    public const string CurrentVault = global::TOTP.Localization.CommonStringKeys.MigrationCurrentVault;
    public const string DataAndBackup = global::TOTP.Localization.CommonStringKeys.MigrationDataAndBackup;
    public const string InputDuplicates = global::TOTP.Localization.CommonStringKeys.MigrationInputDuplicates;
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
