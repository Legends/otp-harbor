using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using TOTP.Core.Backup;
using TOTP.Core.Models;
using TOTP.Core.Security;
using TOTP.Core.Security.Interfaces;
using TOTP.Infrastructure.Security;

namespace OtpHarbor.Testing.StoreUpgrade;

public static class UpgradeVerifier
{
    // Public, fictional test seeds. Never populate this kit with personal accounts.
    public static IReadOnlyList<Account> FixtureAccounts() =>
    [
        new(Guid.Empty, "Upgrade Alpha", "JBSWY3DPEHPK3PXP", "alpha@example.invalid", 30),
        new(Guid.Empty, "Upgrade Beta", "KRSXG5DSNFXGOIDB", "beta@example.invalid", 60),
        new(Guid.Empty, "Upgrade München", "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", "jörg@example.invalid", 15),
        new(Guid.Empty, "Upgrade Delta", "MFRGGZDFMZTWQ2LK", "delta@example.invalid", 30)
    ];

    public static async Task VerifyV2Async(string vaultPath, string envelopePath, string password)
    {
        var accounts = await ReadAsync(vaultPath, envelopePath, password, "TVLT");
        ValidateFixture(accounts);
    }

    public static async Task VerifyV3Async(
        string oldVaultPath, string oldEnvelopePath, string vaultPath, string envelopePath, string password)
    {
        AssertSameFile(oldEnvelopePath, envelopePath);
        var before = await ReadAsync(oldVaultPath, oldEnvelopePath, password, "TVLT");
        var after = await ReadAsync(vaultPath, envelopePath, password, "TVL2");
        ValidateFixture(before);
        ValidateFixture(after);
        Require(before.Count == after.Count, "Account count changed.");
        foreach (var original in before)
        {
            var migrated = after.SingleOrDefault(account => account.ID == original.ID);
            Require(migrated is not null && SameFields(original, migrated),
                "An account ID or supported field changed during migration.");
        }

        var rollback = Path.Combine(Path.GetDirectoryName(vaultPath)!, "pre-upgrade-rollback");
        AssertSameFile(oldVaultPath, Path.Combine(rollback, "master.totp"));
        AssertSameFile(oldEnvelopePath, Path.Combine(rollback, "authorization-envelope.bin"));
        // Read the rollback copy with the original password as a recovery check.
        await VerifyV2Async(Path.Combine(rollback, "master.totp"),
            Path.Combine(rollback, "authorization-envelope.bin"), password);
    }

    public static void AssertSameFile(string first, string second)
    {
        var left = ReadBounded(first, AutomaticBackupPackageV1Codec.MaximumVaultSize);
        byte[]? right = null;
        try
        {
            right = ReadBounded(second, AutomaticBackupPackageV1Codec.MaximumVaultSize);
            Require(CryptographicOperations.FixedTimeEquals(left, right),
                "An encrypted baseline, authorization envelope or rollback file changed.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(left);
            if (right is not null) CryptographicOperations.ZeroMemory(right);
        }
    }

    private static async Task<IReadOnlyList<Account>> ReadAsync(
        string vaultPath, string envelopePath, string password, string header)
    {
        var vault = ReadBounded(vaultPath, AutomaticBackupPackageV1Codec.MaximumVaultSize);
        byte[]? envelopeBytes = null;
        byte[]? key = null;
        TOTP.Core.Security.Models.AuthorizationEnvelopeV2? envelope = null;
        try
        {
            Require(vault.AsSpan().StartsWith(System.Text.Encoding.ASCII.GetBytes(header)),
                "The vault has an unexpected format; close the app and check the selected test phase.");
            envelopeBytes = ReadBounded(envelopePath, AuthorizationEnvelopeV2Codec.MaximumPayloadSize);
            var decoded = AuthorizationEnvelopeV2Codec.Deserialize(envelopeBytes);
            Require(decoded.IsSuccess, "The authorization envelope is invalid.");
            envelope = decoded.Value;
            var passwords = new MasterPasswordService(NullLogger<MasterPasswordService>.Instance);
            key = await passwords.UnwrapKeyV2Async(envelope.PasswordWrapper, password);
            Require(key is not null, "The original test password could not unlock the vault.");
            var result = new VaultService(new ReadOnlyContext()).ReadAccounts(vault, key!);
            Require(result.IsSuccess, "Vault authentication or account decoding failed.");
            return result.Value;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vault);
            if (envelopeBytes is not null) CryptographicOperations.ZeroMemory(envelopeBytes);
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (envelope is not null)
            {
                CryptographicOperations.ZeroMemory(envelope.PasswordWrapper.Kdf.Salt);
                CryptographicOperations.ZeroMemory(envelope.PasswordWrapper.WrappedKey.Nonce);
                CryptographicOperations.ZeroMemory(envelope.PasswordWrapper.WrappedKey.Ciphertext);
                var quick = envelope.QuickUnlockWrapper?.WrappedKey;
                if (quick?.Nonce is not null) CryptographicOperations.ZeroMemory(quick.Nonce);
                if (quick is not null) CryptographicOperations.ZeroMemory(quick.Ciphertext);
            }
        }
    }

    private static void ValidateFixture(IReadOnlyList<Account> accounts)
    {
        var expected = FixtureAccounts();
        Require(accounts.Count == expected.Count && accounts.All(account => account.ID != Guid.Empty)
            && accounts.Select(account => account.ID).Distinct().Count() == accounts.Count,
            "Only the four synthetic upgrade accounts may be present in the test vault.");
        foreach (var fixture in expected)
            Require(accounts.Count(account => SameFields(fixture, account)) == 1,
                "The vault does not exactly match the synthetic fixture; no snapshot was approved.");
    }

    private static bool SameFields(Account left, Account right) =>
        left.Issuer == right.Issuer && left.AccountName == right.AccountName
        && left.Secret == right.Secret && left.Algorithm == right.Algorithm
        && left.Digits == right.Digits && left.PeriodSeconds == right.PeriodSeconds
        && left.IsFavorite == right.IsFavorite && left.Groups.SequenceEqual(right.Groups);

    private static byte[] ReadBounded(string path, int maximumBytes)
    {
        // Refuse links in every existing ancestor, as well as oversized/unexpected files.
        for (var candidate = Path.GetFullPath(path); candidate is not null; candidate = Path.GetDirectoryName(candidate))
            Require((File.GetAttributes(candidate) & FileAttributes.ReparsePoint) == 0,
                "A test data path contains a reparse point.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require(stream.Length > 0 && stream.Length <= maximumBytes, "A test data file has an invalid size.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private sealed class ReadOnlyContext : ISecurityContext
    {
        public bool IsUnlocked => false;
        public byte[] GetDekCopy() => throw new InvalidOperationException();
        public void SetDek(byte[] dek) => throw new InvalidOperationException();
        public void Lock() => throw new InvalidOperationException();
    }
}
