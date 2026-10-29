using FluentResults;

namespace TOTP.Core.Security.Models;

public sealed class AuthorizationEnvelopeActivationError : Error
{
    public const string ErrorCodeMetadataKey = "AuthorizationEnvelopeActivationErrorCode";

    public AuthorizationEnvelopeActivationError(
        AuthorizationEnvelopeActivationErrorCode code,
        string message,
        Exception? exception = null)
        : base(message)
    {
        Metadata[ErrorCodeMetadataKey] = code;
        if (exception is not null) CausedBy(exception);
    }

    public AuthorizationEnvelopeActivationErrorCode Code =>
        Metadata.TryGetValue(ErrorCodeMetadataKey, out var value)
        && value is AuthorizationEnvelopeActivationErrorCode code
            ? code
            : AuthorizationEnvelopeActivationErrorCode.Unknown;
}
