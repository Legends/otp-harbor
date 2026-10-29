using System.Security.Cryptography;
using TOTP.Core.Security.Models;

namespace TOTP.Infrastructure.Security;

internal static class AuthorizationEnvelopeBufferCleaner
{
    public static void Clear(AuthorizationEnvelopeV2 envelope)
    {
        CryptographicOperations.ZeroMemory(envelope.PasswordWrapper.Kdf.Salt);
        CryptographicOperations.ZeroMemory(envelope.PasswordWrapper.WrappedKey.Nonce);
        CryptographicOperations.ZeroMemory(envelope.PasswordWrapper.WrappedKey.Ciphertext);
        if (envelope.QuickUnlockWrapper is not null)
            Clear(envelope.QuickUnlockWrapper);
    }

    public static void Clear(PlatformQuickUnlockWrapperV2 wrapper)
    {
        var platformWrapper = wrapper.WrappedKey;
        if (platformWrapper?.Nonce is not null)
            CryptographicOperations.ZeroMemory(platformWrapper.Nonce);
        if (platformWrapper is not null)
            CryptographicOperations.ZeroMemory(platformWrapper.Ciphertext);
    }
}
