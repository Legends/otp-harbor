namespace TOTP.Core.Security.Models;

public enum AuthorizationEnvelopePasswordLifecycleErrorCode
{
    Unknown = 0,
    InvalidNewPassword,
    CurrentPasswordRequired,
    EnvelopeLoadFailed,
    AlreadyConfigured,
    NotConfigured,
    InvalidCurrentPassword,
    InvalidRecoveredKey,
    ActivationFailed,
    UnexpectedFailure
}
