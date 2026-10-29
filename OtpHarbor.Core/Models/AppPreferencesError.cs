using FluentResults;

namespace TOTP.Core.Models;

public sealed class AppPreferencesError : Error
{
    public const string ErrorCodeMetadataKey = "AppPreferencesErrorCode";

    public AppPreferencesError(AppPreferencesErrorCode code, string message, Exception? exception = null)
        : base(message)
    {
        Metadata[ErrorCodeMetadataKey] = code;
        if (exception is not null)
        {
            CausedBy(exception);
        }
    }

    public AppPreferencesErrorCode Code =>
        Metadata.TryGetValue(ErrorCodeMetadataKey, out var value) && value is AppPreferencesErrorCode code
            ? code
            : AppPreferencesErrorCode.Unknown;
}
