namespace TOTP.Core.Security.Interfaces;

/// <summary>
/// Device-local vault-key protection that deliberately does not require user
/// verification. Implementations must still use operating-system-protected
/// storage and are only enrolled after recovery-password verification or fresh
/// platform authorization of an unlocked session whose key verifies the vault.
/// </summary>
public interface IPlatformUnattendedUnlock : IPlatformQuickUnlock;
