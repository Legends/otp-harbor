namespace TOTP.Core.Security.Models;

public enum PlatformQuickUnlockEnrollmentErrorCode
{
    Unknown = 0,
    RecoveryPasswordRequired,
    EnvelopeLoadFailed,
    NotConfigured,
    AlreadyEnabled,
    InvalidRecoveryPassword,
    InvalidRecoveredKey,
    VaultVerificationFailed,
    PlatformUnavailable,
    RegistrationFailed,
    PersistenceFailed,
    CleanupFailed,
    UnexpectedFailure
}
