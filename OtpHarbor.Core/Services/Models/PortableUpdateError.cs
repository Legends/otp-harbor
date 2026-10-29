using FluentResults;

namespace TOTP.Core.Services.Models;

public sealed class PortableUpdateError : Error
{
    public const string ErrorCodeMetadataKey = "PortableUpdateErrorCode";

    public PortableUpdateError(
        PortableUpdateErrorCode code,
        string message,
        Exception? exception = null)
        : base(message)
    {
        Metadata[ErrorCodeMetadataKey] = code;
        if (exception is not null) CausedBy(exception);
    }

    public PortableUpdateErrorCode Code =>
        Metadata.TryGetValue(ErrorCodeMetadataKey, out var value)
        && value is PortableUpdateErrorCode code
            ? code
            : PortableUpdateErrorCode.Unknown;
}
