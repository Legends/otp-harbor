using System.Security.Cryptography;
using TOTP.Core.Common;

namespace TOTP.DAL.Common;

internal static class AccountDalErrorMapper
{
    public static AppError MapReadError(Exception ex) =>
        ex switch
        {
            UnauthorizedAccessException => new AppError(AppErrorCode.OtpStorageAccessDenied, "Access denied while reading OTP storage.", ex),
            CryptographicException => new AppError(AppErrorCode.OtpStorageDecryptFailed, "Failed to decrypt OTP storage.", ex),
            IOException => new AppError(AppErrorCode.OtpStorageReadFailed, "I/O error while reading OTP storage.", ex),
            _ => new AppError(AppErrorCode.OtpStorageReadFailed, "Unexpected error while reading OTP storage.", ex)
        };

    public static AppError MapWriteError(Exception ex, AppErrorCode operationCode, string operationMessage) =>
        ex switch
        {
            UnauthorizedAccessException => new AppError(AppErrorCode.OtpStorageAccessDenied, "Access denied while writing OTP storage.", ex),
            CryptographicException => new AppError(AppErrorCode.OtpStorageEncryptionFailed, "Failed to encrypt OTP storage.", ex),
            IOException => new AppError(AppErrorCode.OtpStorageWriteFailed, "I/O error while writing OTP storage.", ex),
            _ => new AppError(operationCode, operationMessage, ex)
        };

    public static AppError MapExportError(Exception ex) =>
        ex switch
        {
            UnauthorizedAccessException => new AppError(AppErrorCode.ExportFileAccessDenied, "Access denied while exporting encrypted OTP data.", ex),
            DirectoryNotFoundException => new AppError(AppErrorCode.ExportFileWriteFailed, "Export directory was not found.", ex),
            CryptographicException => new AppError(AppErrorCode.ExportEncryptionFailed, "Failed to encrypt OTP export.", ex),
            IOException => new AppError(AppErrorCode.ExportFileWriteFailed, "I/O error while exporting OTP data.", ex),
            _ => new AppError(AppErrorCode.ExportUnknownFailed, "Unexpected error while exporting OTP data.", ex)
        };
}
