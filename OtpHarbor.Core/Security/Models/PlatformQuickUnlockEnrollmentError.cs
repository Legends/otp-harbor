using FluentResults;

namespace TOTP.Core.Security.Models;

public sealed class PlatformQuickUnlockEnrollmentError : Error
{
    public const string ErrorCodeMetadataKey = "PlatformQuickUnlockEnrollmentErrorCode";

    public PlatformQuickUnlockEnrollmentError(
        PlatformQuickUnlockEnrollmentErrorCode code,
        string message,
        Exception? exception = null)
        : base(message)
    {
        Metadata[ErrorCodeMetadataKey] = code;
        if (exception is not null) CausedBy(exception);
    }

    public PlatformQuickUnlockEnrollmentErrorCode Code =>
        Metadata.TryGetValue(ErrorCodeMetadataKey, out var value)
        && value is PlatformQuickUnlockEnrollmentErrorCode code
            ? code
            : PlatformQuickUnlockEnrollmentErrorCode.Unknown;
}
