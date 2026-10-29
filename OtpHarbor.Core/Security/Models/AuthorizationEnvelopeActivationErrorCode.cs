namespace TOTP.Core.Security.Models;

public enum AuthorizationEnvelopeActivationErrorCode
{
    Unknown = 0,
    InvalidEnvelope,
    InvalidCandidateKey,
    PasswordWrapperRejected,
    CandidateKeyMismatch,
    VaultVerificationFailed,
    PersistenceFailed,
    UnexpectedFailure
}
