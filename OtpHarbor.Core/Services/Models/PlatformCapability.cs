namespace TOTP.Core.Services.Models;

public enum PlatformCapabilityStatus
{
    Failed = 0,
    Supported,
    TemporarilyUnavailable,
    PermanentlyUnavailable,
    PermissionDenied,
    Misconfigured
}

public sealed record PlatformCapability(string Name, PlatformCapabilityStatus Status);
