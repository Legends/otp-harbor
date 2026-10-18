using NSec.Cryptography;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOTP.Core.Models;
using TOTP.Core.Backup;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using FluentResults;

namespace TOTP.Infrastructure.Security;

public sealed class VaultService : IVaultService, IVaultKeyVerifier, IVaultSnapshotReader
{
    private readonly ISecurityContext _securityContext;
    private static readonly AeadAlgorithm Algorithm = AeadAlgorithm.Aes256Gcm;
    private static readonly byte[] FileHeader = "TVLT"u8.ToArray();
    private const int VaultKeySize = 32;

    public VaultService(ISecurityContext securityContext)
    {
        _securityContext = securityContext ?? throw new ArgumentNullException(nameof(securityContext));
    }

    public byte[] EncryptVault(List<Account> entries)
    {
        if (!_securityContext.IsUnlocked) throw new InvalidOperationException("Security context locked.");

        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        byte[] data = Encoding.UTF8.GetBytes(json);
        byte[] nonce = RandomNumberGenerator.GetBytes(Algorithm.NonceSize);
        var dek = _securityContext.GetDekCopy();
        try
        {
            using var key = Key.Import(Algorithm, dek, KeyBlobFormat.RawSymmetricKey);
            byte[] encrypted = Algorithm.Encrypt(key, nonce, null, data);

            // Combine: Header + Nonce + Ciphertext
            byte[] result = new byte[FileHeader.Length + nonce.Length + encrypted.Length];
            Buffer.BlockCopy(FileHeader, 0, result, 0, FileHeader.Length);
            Buffer.BlockCopy(nonce, 0, result, FileHeader.Length, nonce.Length);
            Buffer.BlockCopy(encrypted, 0, result, FileHeader.Length + nonce.Length, encrypted.Length);

            return result;
        }
        finally
        {
            Array.Clear(dek, 0, dek.Length);
            Array.Clear(data, 0, data.Length);
        }
    }

    public List<Account> DecryptVault(byte[] encryptedBlob)
    {
        if (!_securityContext.IsUnlocked) throw new InvalidOperationException("Security context locked.");
        var dek = _securityContext.GetDekCopy();
        try
        {
            return DecryptVaultWithKey(encryptedBlob, dek);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    public Result<IReadOnlyList<Account>> ReadAccounts(
        ReadOnlySpan<byte> encryptedVault,
        ReadOnlySpan<byte> vaultKey)
    {
        try
        {
            return Result.Ok<IReadOnlyList<Account>>(
                DecryptVaultWithKey(encryptedVault, vaultKey));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            return Result.Fail<IReadOnlyList<Account>>(
                new AutomaticBackupError(
                    AutomaticBackupErrorCode.InvalidVault,
                    "Automatic backup vault authentication failed.",
                    ex));
        }
    }

    private static List<Account> DecryptVaultWithKey(
        ReadOnlySpan<byte> encryptedBlob,
        ReadOnlySpan<byte> vaultKey)
    {
        var minimumSize = FileHeader.Length + Algorithm.NonceSize + Algorithm.TagSize;
        if (encryptedBlob.Length < minimumSize)
            throw new CryptographicException("Data is too small or corrupted.");

        if (!encryptedBlob[..FileHeader.Length].SequenceEqual(FileHeader))
            throw new CryptographicException("Invalid file header.");

        if (vaultKey.Length != VaultKeySize)
            throw new CryptographicException("Invalid vault key.");

        var nonce = encryptedBlob.Slice(FileHeader.Length, Algorithm.NonceSize);
        var ciphertext = encryptedBlob[(FileHeader.Length + Algorithm.NonceSize)..];
        byte[]? decrypted = null;
        try
        {
            using var key = Key.Import(Algorithm, vaultKey, KeyBlobFormat.RawSymmetricKey);
            decrypted = Algorithm.Decrypt(key, nonce, ReadOnlySpan<byte>.Empty, ciphertext);
            if (decrypted is null)
                throw new CryptographicException("Decryption failed.");

            return JsonSerializer.Deserialize<List<Account>>(decrypted)
                ?? throw new JsonException("Vault account payload is null.");
        }
        finally
        {
            if (decrypted is not null) CryptographicOperations.ZeroMemory(decrypted);
        }
    }

    public VaultKeyVerificationStatus Verify(
        ReadOnlySpan<byte> encryptedVault,
        ReadOnlySpan<byte> candidateKey)
    {
        if (candidateKey.Length != VaultKeySize)
            return VaultKeyVerificationStatus.InvalidCandidateKey;

        var minimumSize = FileHeader.Length + Algorithm.NonceSize + Algorithm.TagSize;
        if (encryptedVault.Length < minimumSize
            || !encryptedVault[..FileHeader.Length].SequenceEqual(FileHeader))
        {
            return VaultKeyVerificationStatus.InvalidVaultFormat;
        }

        var nonce = encryptedVault.Slice(FileHeader.Length, Algorithm.NonceSize);
        var ciphertext = encryptedVault[(FileHeader.Length + Algorithm.NonceSize)..];
        byte[]? plaintext = null;
        try
        {
            using var key = Key.Import(Algorithm, candidateKey, KeyBlobFormat.RawSymmetricKey);
            plaintext = Algorithm.Decrypt(key, nonce, ReadOnlySpan<byte>.Empty, ciphertext);
            if (plaintext is null)
                return VaultKeyVerificationStatus.AuthenticationFailed;

            try
            {
                return JsonSerializer.Deserialize<List<Account>>(plaintext) is not null
                    ? VaultKeyVerificationStatus.Verified
                    : VaultKeyVerificationStatus.InvalidVaultFormat;
            }
            catch (JsonException)
            {
                return VaultKeyVerificationStatus.InvalidVaultFormat;
            }
        }
        catch (CryptographicException)
        {
            return VaultKeyVerificationStatus.AuthenticationFailed;
        }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
