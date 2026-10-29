using FluentResults;

namespace TOTP.Core.Security.Models;

public sealed class PlatformQuickUnlockError : Error
{
    public const string ErrorCodeMetadataKey = "PlatformQuickUnlockErrorCode";

    public PlatformQuickUnlockError(
        PlatformQuickUnlockErrorCode code,
        string message,
        Exception? exception = null)
        : base(message)
    {
        Metadata[ErrorCodeMetadataKey] = code;
        if (exception is not null)
        {
            CausedBy(exception);
        }
    }

    public PlatformQuickUnlockErrorCode Code =>
        Metadata.TryGetValue(ErrorCodeMetadataKey, out var value)
            && value is PlatformQuickUnlockErrorCode code
                ? code
                : PlatformQuickUnlockErrorCode.Unknown;
}
