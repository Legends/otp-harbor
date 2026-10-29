using FluentResults;

namespace TOTP.Core.Security.Models;

public sealed class AuthorizationEnvelopeSessionError : Error
{
    public const string ErrorCodeMetadataKey = "AuthorizationEnvelopeSessionErrorCode";

    public AuthorizationEnvelopeSessionError(
        AuthorizationEnvelopeSessionErrorCode code,
        string message,
        Exception? exception = null)
        : base(message)
    {
        Metadata[ErrorCodeMetadataKey] = code;
        if (exception is not null) CausedBy(exception);
    }

    public AuthorizationEnvelopeSessionErrorCode Code =>
        Metadata.TryGetValue(ErrorCodeMetadataKey, out var value)
        && value is AuthorizationEnvelopeSessionErrorCode code
            ? code
            : AuthorizationEnvelopeSessionErrorCode.Unknown;
}
