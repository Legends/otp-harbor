using FluentResults;

namespace TOTP.Core.Services.Models;

public enum AccountImportFormat
{
    Unknown, OtpHarborJson, OtpHarborCsv, OtpHarborText, AegisJson, TwoFasJson,
    BitwardenJson, BitwardenCsv, ProtonJson, TotpUriList, EncryptedExternalExport, EncryptedOtpHarborBackup
}

public sealed class AccountImportReadSummary(int supportedCount, int invalidCount = 0, int unsupportedCount = 0, int nonOtpCount = 0)
    : Success("Authenticator import validation summary.")
{
    public int SupportedCount { get; } = supportedCount;
    public int InvalidCount { get; } = invalidCount;
    public int UnsupportedCount { get; } = unsupportedCount;
    public int NonOtpCount { get; } = nonOtpCount;
    public AccountImportFormat Format { get; init; }
}

public sealed class AccountImportReadError(AccountImportReadSummary summary)
    : Error("The authenticator export contains invalid or unsupported entries.")
{
    public AccountImportReadSummary Summary { get; } = summary;
}

public sealed class AccountImportReadCancelled() : Error("Account import format selection cancelled.");
