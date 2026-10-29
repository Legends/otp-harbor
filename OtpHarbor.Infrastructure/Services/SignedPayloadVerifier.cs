using System.Security.Cryptography;
using NSec.Cryptography;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Infrastructure.Services;

public sealed class SignedPayloadVerifier : ISignedPayloadVerifier
{
    public bool Verify(ReadOnlySpan<byte> payload, string signature, string publicKey)
    {
        if (payload.IsEmpty || string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(publicKey))
            return false;

        byte[]? publicKeyBytes = null;
        byte[]? signatureBytes = null;
        try
        {
            publicKeyBytes = Convert.FromBase64String(publicKey.Trim());
            signatureBytes = Convert.FromBase64String(signature.Trim());
            if (publicKeyBytes.Length != 32 || signatureBytes.Length != 64) return false;

            var importedKey = PublicKey.Import(
                SignatureAlgorithm.Ed25519,
                publicKeyBytes,
                KeyBlobFormat.RawPublicKey);
            return SignatureAlgorithm.Ed25519.Verify(importedKey, payload, signatureBytes);
        }
        catch (Exception exception) when (exception is FormatException
                                          or CryptographicException
                                          or ArgumentException)
        {
            return false;
        }
        finally
        {
            if (publicKeyBytes is not null) CryptographicOperations.ZeroMemory(publicKeyBytes);
            if (signatureBytes is not null) CryptographicOperations.ZeroMemory(signatureBytes);
        }
    }
}
