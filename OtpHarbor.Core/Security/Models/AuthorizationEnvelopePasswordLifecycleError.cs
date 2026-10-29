using FluentResults;

namespace TOTP.Core.Security.Models;

public sealed class AuthorizationEnvelopePasswordLifecycleError : Error
{
    public const string ErrorCodeMetadataKey = "AuthorizationEnvelopePasswordLifecycleErrorCode";

    public AuthorizationEnvelopePasswordLifecycleError(
        AuthorizationEnvelopePasswordLifecycleErrorCode code,
        string message,
        Exception? exception = null)
        : base(message)
    {
        Metadata[ErrorCodeMetadataKey] = code;
        if (exception is not null) CausedBy(exception);
    }

    public AuthorizationEnvelopePasswordLifecycleErrorCode Code =>
        Metadata.TryGetValue(ErrorCodeMetadataKey, out var value)
        && value is AuthorizationEnvelopePasswordLifecycleErrorCode code
            ? code
            : AuthorizationEnvelopePasswordLifecycleErrorCode.Unknown;
}
