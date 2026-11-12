using TOTP.Core.Services.Models;

namespace TOTP.Avalonia.Shared.Import;

public static class FileImportStringKeys
{
    public const string FileTitle = "FileImportFileTitle";
    public const string AutoDetection = "FileImportAutoDetection";
    public const string SupportedFormats = "FileImportSupportedFormats";
    public const string HelpTitle = "FileImportHelpTitle";
    public const string HelpDescription = "FileImportHelpDescription";
    public const string OptionalSource = "FileImportOptionalSource";
    public const string DetectedFormat = "FileImportDetectedFormat";
    public const string TotalEntries = "FileImportTotalEntries";
    public const string ValidEntries = "FileImportValidEntries";
    public const string AttentionEntries = "FileImportAttentionEntries";
    public const string ReviewEntries = "FileImportReviewEntries";
    public const string ClearFile = "FileImportClearFile";
    public const string AmbiguousFormat = "FileImportAmbiguousFormat";
    public const string ChooseFormat = "FileImportChooseFormat";
    public const string UseFormat = "FileImportUseFormat";
    public const string AllFiles = "FileImportAllFiles";
    public static string Format(AccountImportFormat format) => "FileImportFormat" + format;
    public static IReadOnlyList<string> All { get; } = [ AllFiles, AmbiguousFormat, ChooseFormat, UseFormat, FileTitle, AutoDetection, SupportedFormats, HelpTitle, HelpDescription, OptionalSource, DetectedFormat, TotalEntries, ValidEntries, AttentionEntries, ReviewEntries, ClearFile, .. Enum.GetValues<AccountImportFormat>().Select(Format) ];
}
