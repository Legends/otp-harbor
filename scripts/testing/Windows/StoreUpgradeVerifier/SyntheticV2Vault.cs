using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSec.Cryptography;
using TOTP.Core.Security;
using TOTP.Core.Security.Models;
using TOTP.Infrastructure.Security;
using TOTP.Platform.Windows;

namespace OtpHarbor.Testing.StoreUpgrade;

public static class SyntheticV2Vault
{
    // Public credentials for fictional data only, never a personal vault password.
    public const string Password = "OTP-Harbor-v2-test-only!";

    public static void WriteImportFixture(string path)
    {
        var lines = new[] { "issuer|account_name|secret|id|period_seconds" }.Concat(
            UpgradeVerifier.FixtureAccounts().Select((account, index) =>
                $"{account.Issuer}|{account.AccountName}|{account.Secret}|00000000-0000-4000-8000-{index + 1:000000000000}|{account.PeriodSeconds}"));
        File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(false));
    }

    public static async Task CreateAsync(string outputDirectory)
    {
        var root = Path.GetFullPath(outputDirectory);
        if (File.Exists(root) || Directory.Exists(root))
            throw new InvalidDataException("The synthetic fixture destination must not exist.");
        for (var ancestor = Path.GetDirectoryName(root); ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
            if (Directory.Exists(ancestor) && (File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The synthetic fixture path contains a reparse point.");

        Directory.CreateDirectory(root);
        new WindowsFileSecurity().RestrictDirectoryToCurrentUser(root);
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        byte[]? plain = null;
        byte[]? ciphertext = null;
        byte[]? vault = null;
        byte[]? envelopeBytes = null;
        AuthorizationEnvelopeV2? envelope = null;
        try
        {
            // The five fields and TVLT + nonce + AES-GCM ciphertext/tag layout are
            // taken from the released v2.0.1 Account and VaultService implementations.
            plain = JsonSerializer.SerializeToUtf8Bytes(UpgradeVerifier.FixtureAccounts().Select((account, index) => new
            {
                id = Guid.Parse($"00000000-0000-4000-8000-{index + 1:000000000000}"),
                issuer = account.Issuer, secret = account.Secret,
                account_name = account.AccountName, period = account.PeriodSeconds
            }));
            using (var key = Key.Import(AeadAlgorithm.Aes256Gcm, keyBytes, KeyBlobFormat.RawSymmetricKey))
                ciphertext = AeadAlgorithm.Aes256Gcm.Encrypt(key, nonce, ReadOnlySpan<byte>.Empty, plain);
            vault = "TVLT"u8.ToArray().Concat(nonce).Concat(ciphertext).ToArray();
            var passwords = new MasterPasswordService(NullLogger<MasterPasswordService>.Instance);
            envelope = new AuthorizationEnvelopeV2
            {
                PasswordWrapper = await passwords.WrapKeyV2Async(keyBytes, Password, CancellationToken.None)
            };
            var encoded = AuthorizationEnvelopeV2Codec.Serialize(envelope);
            if (encoded.IsFailed) throw new InvalidDataException("Synthetic envelope creation failed.");
            envelopeBytes = encoded.Value;
            WriteNew(Path.Combine(root, "master.totp"), vault);
            WriteNew(Path.Combine(root, "master.totp.bak1"), vault);
            WriteNew(Path.Combine(root, "authorization-envelope.bin"), envelopeBytes);
            await UpgradeVerifier.VerifyV2Async(Path.Combine(root, "master.totp"),
                Path.Combine(root, "authorization-envelope.bin"), Password);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            CryptographicOperations.ZeroMemory(nonce);
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            if (ciphertext is not null) CryptographicOperations.ZeroMemory(ciphertext);
            if (vault is not null) CryptographicOperations.ZeroMemory(vault);
            if (envelopeBytes is not null) CryptographicOperations.ZeroMemory(envelopeBytes);
            if (envelope is not null)
            {
                CryptographicOperations.ZeroMemory(envelope.PasswordWrapper.Kdf.Salt);
                CryptographicOperations.ZeroMemory(envelope.PasswordWrapper.WrappedKey.Nonce);
                CryptographicOperations.ZeroMemory(envelope.PasswordWrapper.WrappedKey.Ciphertext);
            }
        }
    }

    private static void WriteNew(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }
}
