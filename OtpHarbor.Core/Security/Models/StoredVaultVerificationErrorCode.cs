namespace TOTP.Core.Security.Models;

public enum StoredVaultVerificationErrorCode
{
    Unknown = 0,
    TooLarge,
    ReadAccessDenied,
    ReadFailed
}
