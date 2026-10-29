using FluentResults;

namespace TOTP.Core.Security.Models;

public sealed class PlatformSecretStoreError : Error
{
    public const string ErrorCodeMetadataKey = "PlatformSecretStoreErrorCode";

    public PlatformSecretStoreError(
        PlatformSecretStoreErrorCode code,
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

    public PlatformSecretStoreErrorCode Code =>
        Metadata.TryGetValue(ErrorCodeMetadataKey, out var value)
            && value is PlatformSecretStoreErrorCode code
                ? code
                : PlatformSecretStoreErrorCode.Unknown;
}
