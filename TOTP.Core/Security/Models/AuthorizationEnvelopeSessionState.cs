namespace TOTP.Core.Security.Models;

public sealed record AuthorizationEnvelopeSessionState(
    bool IsInitialized,
    bool IsConfigured,
    bool HasQuickUnlock,
    bool HasUnattendedUnlock = false)
{
    public static AuthorizationEnvelopeSessionState NotInitialized { get; } = new(false, false, false);
}
