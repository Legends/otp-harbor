using FluentResults;

namespace TOTP.Core.Security.Models;

public sealed class AuthorizationEnvelopeError : Error
{
    public const string ErrorCodeMetadataKey = "AuthorizationEnvelopeErrorCode";

    public AuthorizationEnvelopeError(AuthorizationEnvelopeErrorCode code, string message, Exception? exception = null)
        : base(message)
    {
        Metadata[ErrorCodeMetadataKey] = code;
        if (exception is not null)
        {
            CausedBy(exception);
        }
    }

    public AuthorizationEnvelopeErrorCode Code =>
        Metadata.TryGetValue(ErrorCodeMetadataKey, out var value) && value is AuthorizationEnvelopeErrorCode code
            ? code
            : AuthorizationEnvelopeErrorCode.Unknown;
}
