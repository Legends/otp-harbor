namespace TOTP.Core.Security.Models;

public enum PlatformQuickUnlockStatus
{
    Unknown = 0,
    Succeeded,
    Cancelled,
    NotAvailable,
    NotConfigured,
    DisabledByPolicy,
    RetriesExhausted,
    VerificationFailed,
    KeyNotFound
}
