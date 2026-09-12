using TOTP.Core.Enums;

namespace TOTP.Core.Security.Models;

public sealed record AuthorizationEnvelopeSessionState(
    bool IsInitialized,
    bool IsConfigured,
    bool HasQuickUnlock,
    bool HasUnattendedUnlock = false,
    PreferredUnlockMethod? PlatformUnlockMethod = null)
{
    public static AuthorizationEnvelopeSessionState NotInitialized { get; } = new(false, false, false);
}
