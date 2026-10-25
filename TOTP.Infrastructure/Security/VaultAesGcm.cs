using System.Security.Cryptography;

namespace TOTP.Infrastructure.Security;

internal static class VaultAesGcm
{
    internal const int KeySize = 32;
    internal const int NonceSize = 12;
    internal const int TagSize = 16;

    internal static byte[] Encrypt(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> associatedData,
        ReadOnlySpan<byte> plaintext)
    {
        ValidateKeyAndNonce(key, nonce);

        var encrypted = new byte[checked(plaintext.Length + TagSize)];
        try
        {
            using var algorithm = new AesGcm(key, TagSize);
            algorithm.Encrypt(
                nonce,
                plaintext,
                encrypted.AsSpan(0, plaintext.Length),
                encrypted.AsSpan(plaintext.Length, TagSize),
                associatedData);
            return encrypted;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(encrypted);
            throw;
        }
    }

    internal static byte[] Decrypt(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> associatedData,
        ReadOnlySpan<byte> encrypted)
    {
        ValidateKeyAndNonce(key, nonce);
        if (encrypted.Length < TagSize)
            throw new CryptographicException("Encrypted vault section is too small.");

        var plaintext = new byte[encrypted.Length - TagSize];
        try
        {
            using var algorithm = new AesGcm(key, TagSize);
            algorithm.Decrypt(
                nonce,
                encrypted[..^TagSize],
                encrypted[^TagSize..],
                plaintext,
                associatedData);
            return plaintext;
        }
        catch (AuthenticationTagMismatchException exception)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new CryptographicException("Decryption failed.", exception);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
    }

    private static void ValidateKeyAndNonce(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce)
    {
        if (key.Length != KeySize)
            throw new CryptographicException("Invalid vault key.");
        if (nonce.Length != NonceSize)
            throw new CryptographicException("Invalid vault nonce.");
    }
}
