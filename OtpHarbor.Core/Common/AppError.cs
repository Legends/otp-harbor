using FluentResults;

namespace TOTP.Core.Common;

public sealed class AppError : Error
{
    public const string ErrorCodeMetadataKey = "ErrorCode";

    public AppError(AppErrorCode code, string message, Exception? exception = null)
        : base(message)
    {
        Metadata[ErrorCodeMetadataKey] = code;
        if (exception != null)
        {
            CausedBy(exception);
        }
    }

    public AppErrorCode Code =>
        Metadata.TryGetValue(ErrorCodeMetadataKey, out var value) && value is AppErrorCode code
            ? code
            : AppErrorCode.Unknown;
}
