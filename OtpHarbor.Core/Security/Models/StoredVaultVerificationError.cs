using FluentResults;

namespace TOTP.Core.Security.Models;

public sealed class StoredVaultVerificationError : Error
{
    public const string ErrorCodeMetadataKey = "StoredVaultVerificationErrorCode";

    public StoredVaultVerificationError(
        StoredVaultVerificationErrorCode code,
        string message,
        Exception? exception = null)
        : base(message)
    {
        Metadata[ErrorCodeMetadataKey] = code;
        if (exception is not null) CausedBy(exception);
    }

    public StoredVaultVerificationErrorCode Code =>
        Metadata.TryGetValue(ErrorCodeMetadataKey, out var value)
        && value is StoredVaultVerificationErrorCode code
            ? code
            : StoredVaultVerificationErrorCode.Unknown;
}
