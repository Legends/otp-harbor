using TOTP.Core.Services.Models;

namespace TOTP.Avalonia.Shared.Import;

public static class FileImportStringKeys
{
    public const string NoActiveAccounts = global::TOTP.Localization.CommonStringKeys.FileImportNoActiveAccounts;
    public const string BackupPasswordTitle = global::TOTP.Localization.CommonStringKeys.FileImportBackupPasswordTitle;
    public const string BackupPasswordHelp = global::TOTP.Localization.CommonStringKeys.FileImportBackupPasswordHelp;
    public const string SnapshotPasswordHelp = global::TOTP.Localization.CommonStringKeys.FileImportSnapshotPasswordHelp;
    public const string UnlockBackup = global::TOTP.Localization.CommonStringKeys.FileImportUnlockBackup;
    public const string BackupUnlockFailed = global::TOTP.Localization.CommonStringKeys.FileImportBackupUnlockFailed;
    public const string FileTitle = global::TOTP.Localization.CommonStringKeys.FileImportFileTitle;
    public const string AutoDetection = global::TOTP.Localization.CommonStringKeys.FileImportAutoDetection;
    public const string SupportedFormats = global::TOTP.Localization.CommonStringKeys.FileImportSupportedFormats;
    public const string HelpTitle = global::TOTP.Localization.CommonStringKeys.FileImportHelpTitle;
    public const string HelpDescription = global::TOTP.Localization.CommonStringKeys.FileImportHelpDescription;
    public const string OptionalSource = global::TOTP.Localization.CommonStringKeys.FileImportOptionalSource;
    public const string DetectedFormat = global::TOTP.Localization.CommonStringKeys.FileImportDetectedFormat;
    public const string TotalEntries = global::TOTP.Localization.CommonStringKeys.FileImportTotalEntries;
    public const string ValidEntries = global::TOTP.Localization.CommonStringKeys.FileImportValidEntries;
    public const string AttentionEntries = global::TOTP.Localization.CommonStringKeys.FileImportAttentionEntries;
    public const string ReviewEntries = global::TOTP.Localization.CommonStringKeys.FileImportReviewEntries;
    public const string ClearFile = global::TOTP.Localization.CommonStringKeys.FileImportClearFile;
    public const string AmbiguousFormat = global::TOTP.Localization.CommonStringKeys.FileImportAmbiguousFormat;
    public const string ChooseFormat = global::TOTP.Localization.CommonStringKeys.FileImportChooseFormat;
    public const string UseFormat = global::TOTP.Localization.CommonStringKeys.FileImportUseFormat;
    public const string AllFiles = global::TOTP.Localization.CommonStringKeys.FileImportAllFiles;
    public static string Format(AccountImportFormat format) => "FileImportFormat" + format;
    public static IReadOnlyList<string> All { get; } = [ NoActiveAccounts, BackupPasswordTitle, BackupPasswordHelp, SnapshotPasswordHelp, UnlockBackup, BackupUnlockFailed, AllFiles, AmbiguousFormat, ChooseFormat, UseFormat, FileTitle, AutoDetection, SupportedFormats, HelpTitle, HelpDescription, OptionalSource, DetectedFormat, TotalEntries, ValidEntries, AttentionEntries, ReviewEntries, ClearFile, .. Enum.GetValues<AccountImportFormat>().Select(Format) ];
}
