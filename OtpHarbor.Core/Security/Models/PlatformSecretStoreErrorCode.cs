namespace TOTP.Core.Security.Models;

public enum PlatformSecretStoreErrorCode
{
    Unknown = 0,
    Unavailable,
    InvalidReference,
    InvalidSecret,
    AccessDenied,
    StoreFailed,
    RetrieveFailed,
    DeleteFailed
}
