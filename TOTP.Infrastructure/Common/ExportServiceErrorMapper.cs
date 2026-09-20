using System.Security.Cryptography;
using System.Text.Json;
using TOTP.Core.Common;

namespace TOTP.Infrastructure.Common;

internal static class ExportServiceErrorMapper
{
    public static AppError MapExportError(Exception ex) =>
        ex switch
        {
            UnauthorizedAccessException => new AppError(AppErrorCode.ExportFileAccessDenied, "Access denied while writing export file.", ex),
            DirectoryNotFoundException => new AppError(AppErrorCode.ExportFileWriteFailed, "Export directory was not found.", ex),
            IOException => new AppError(AppErrorCode.ExportFileWriteFailed, "I/O error while writing export file.", ex),
            CryptographicException => new AppError(AppErrorCode.ExportEncryptionFailed, "Failed to encrypt export payload.", ex),
            _ => new AppError(AppErrorCode.ExportUnknownFailed, "Unexpected error during export.", ex)
        };

    public static AppError MapImportError(Exception ex) =>
        ex switch
        {
            FileNotFoundException => new AppError(AppErrorCode.ImportFileNotFound, "Import file was not found.", ex),
            DirectoryNotFoundException => new AppError(AppErrorCode.ImportFileNotFound, "Import directory was not found.", ex),
            UnauthorizedAccessException => new AppError(AppErrorCode.ExportFileAccessDenied, "Access denied while reading import file.", ex),
            CryptographicException => new AppError(AppErrorCode.ImportWrongPasswordOrTampered, "Import decryption failed due to cryptographic error.", ex),
            JsonException => new AppError(AppErrorCode.ImportInvalidPayload, "Import file payload is not valid JSON.", ex),
            FormatException or ArgumentException => new AppError(
                AppErrorCode.ImportInvalidPayload,
                "Import file payload is invalid or unsupported.",
                ex),
            _ => new AppError(AppErrorCode.ImportUnknownFailed, "Unexpected error during import.", ex)
        };
}
