namespace TOTP.Core.Security.Models;

public enum AuthorizationEnvelopeErrorCode
{
    Unknown = 0,
    Empty,
    TooLarge,
    Malformed,
    UnsupportedFormat,
    UnsupportedVersion,
    InvalidPasswordWrapper,
    InvalidQuickUnlockWrapper,
    ReadAccessDenied,
    ReadFailed,
    WriteAccessDenied,
    WriteFailed
}
