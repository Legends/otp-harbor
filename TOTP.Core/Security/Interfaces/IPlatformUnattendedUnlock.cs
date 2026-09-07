namespace TOTP.Core.Security.Interfaces;

/// <summary>
/// Device-local vault-key protection that deliberately does not require user
/// verification. Implementations must still use non-exportable platform key
/// storage and are only enrolled after recovery-password verification.
/// </summary>
public interface IPlatformUnattendedUnlock : IPlatformQuickUnlock;
