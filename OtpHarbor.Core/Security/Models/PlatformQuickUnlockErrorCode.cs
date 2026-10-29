namespace TOTP.Core.Security.Models;

public enum PlatformQuickUnlockErrorCode
{
    Unknown = 0,
    Unavailable,
    InvalidMetadata,
    InvalidKeyMaterial,
    Cancelled,
    DisabledByPolicy,
    RetriesExhausted,
    RegistrationFailed,
    UnlockFailed,
    RemoveFailed
}
