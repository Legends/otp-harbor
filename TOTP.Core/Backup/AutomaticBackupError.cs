using FluentResults;

namespace TOTP.Core.Backup;

public enum AutomaticBackupErrorCode
{
    InvalidFormat,
    TooLarge,
    VaultUnavailable,
    EnvelopeUnavailable,
    InvalidPassword,
    InvalidVault,
    DestinationUnavailable,
    ReadFailed,
    WriteFailed,
    ConfigurationFailed,
    UnexpectedFailure
}

public sealed class AutomaticBackupError : Error
{
    public const string ErrorCodeMetadataKey = "AutomaticBackupErrorCode";

    public AutomaticBackupError(
        AutomaticBackupErrorCode code,
        string message,
        Exception? exception = null)
        : base(message)
    {
        Metadata[ErrorCodeMetadataKey] = code;
        if (exception is not null) CausedBy(exception);
    }

    public AutomaticBackupErrorCode Code =>
        Metadata.TryGetValue(ErrorCodeMetadataKey, out var value)
        && value is AutomaticBackupErrorCode code
            ? code
            : AutomaticBackupErrorCode.UnexpectedFailure;
}
