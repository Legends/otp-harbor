using TOTP.Core.Security.Models;

namespace TOTP.Core.Security.Interfaces;

/// <summary>
/// Verifies that a candidate key can authenticate and open an existing vault
/// without changing the active application security context.
/// </summary>
public interface IVaultKeyVerifier
{
    VaultKeyVerificationStatus Verify(
        ReadOnlySpan<byte> encryptedVault,
        ReadOnlySpan<byte> candidateKey);
}
