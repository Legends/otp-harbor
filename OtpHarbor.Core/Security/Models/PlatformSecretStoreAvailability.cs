namespace TOTP.Core.Security.Models;

public enum PlatformSecretStoreAvailability
{
    Unknown = 0,
    Available,
    NotSupported,
    NotConfigured,
    DisabledByPolicy,
    TemporarilyUnavailable
}
