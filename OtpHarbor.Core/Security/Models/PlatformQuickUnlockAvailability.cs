namespace TOTP.Core.Security.Models;

public enum PlatformQuickUnlockAvailability
{
    Unknown = 0,
    Available,
    NotSupported,
    NotConfigured,
    DisabledByPolicy,
    TemporarilyUnavailable
}
