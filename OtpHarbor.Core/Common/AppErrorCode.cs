namespace TOTP.Core.Common;

public enum AppErrorCode
{
    Unknown = 0,

    // OTP storage and token operations
    OtpStorageReadFailed,
    OtpStorageAccessDenied,
    OtpStorageDecryptFailed,
    OtpStorageWriteFailed,
    OtpStorageEncryptionFailed,
    OtpStorageBackupFailed,
    OtpCreateFailed,
    OtpUpdateFailed,
    OtpDeleteFailed,

    // Encrypted import/export
    ExportFileWriteFailed,
    ExportFileAccessDenied,
    ExportEncryptionFailed,
    ExportUnknownFailed,
    ImportFileNotFound,
    ImportInvalidFile,
    ImportWrongPasswordOrTampered,
    ImportInvalidPayload,
    ImportUnknownFailed,

    // Workflow/service boundaries
    AccountsLoadFailed,
    AccountsCreateFailed,
    AccountsUpdateFailed,
    AccountsDeleteFailed,
    SettingsServiceLoadFailed,
    SettingsServiceSaveFailed,

    // Platform clipboard
    ClipboardUnavailable,
    ClipboardWriteFailed,
    ClipboardClearFailed
}
