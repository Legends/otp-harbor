namespace TOTP.Core.Models;

public enum AppPreferencesErrorCode
{
    Unknown = 0,
    Empty,
    TooLarge,
    Malformed,
    UnsupportedFormat,
    UnsupportedVersion,
    InvalidValue,
    ReadAccessDenied,
    ReadFailed,
    WriteAccessDenied,
    WriteFailed
}
