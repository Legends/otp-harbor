using NSec.Cryptography;
using System;
using System.Buffers.Binary;
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
    private static readonly byte[] LegacyFileHeader = "TVLT"u8.ToArray();
    private static readonly byte[] FileHeader = "TVL2"u8.ToArray();
    private static readonly byte[] AccountsAssociatedData = "TVL2:accounts"u8.ToArray();
    private static readonly byte[] ProfileAssociatedData = "TVL2:profile"u8.ToArray();
    private const int VaultKeySize = 32;
    private const int ProfileVersion = 1;

    public VaultService(ISecurityContext securityContext)
    {
        _securityContext = securityContext ?? throw new ArgumentNullException(nameof(securityContext));
    }

    public byte[] EncryptVault(List<Account> entries)
    {
        if (!_securityContext.IsUnlocked) throw new InvalidOperationException("Security context locked.");

        ArgumentNullException.ThrowIfNull(entries);
        byte[]? accountsData = null;
        byte[]? profileData = null;
        byte[]? encryptedAccounts = null;
        byte[]? encryptedProfile = null;
        byte[] accountsNonce = RandomNumberGenerator.GetBytes(Algorithm.NonceSize);
        byte[] profileNonce = RandomNumberGenerator.GetBytes(Algorithm.NonceSize);
        var dek = _securityContext.GetDekCopy();
        try
        {
            var revisionId = Guid.NewGuid();
            accountsData = JsonSerializer.SerializeToUtf8Bytes(new VaultAccountsPayload(
                revisionId,
                entries.Select(VaultAccountPayload.FromAccount).ToArray()));
            profileData = JsonSerializer.SerializeToUtf8Bytes(
                VaultProfilePayload.FromAccounts(revisionId, entries));
            using var key = Key.Import(Algorithm, dek, KeyBlobFormat.RawSymmetricKey);
            encryptedAccounts = Algorithm.Encrypt(
                key,
                accountsNonce,
                AccountsAssociatedData,
                accountsData);
            encryptedProfile = Algorithm.Encrypt(
                key,
                profileNonce,
                ProfileAssociatedData,
                profileData);

            var prefixLength = FileHeader.Length + sizeof(int)
                + accountsNonce.Length + profileNonce.Length;
            byte[] result = new byte[
                prefixLength + encryptedAccounts.Length + encryptedProfile.Length];
            Buffer.BlockCopy(FileHeader, 0, result, 0, FileHeader.Length);
            BinaryPrimitives.WriteInt32LittleEndian(
                result.AsSpan(FileHeader.Length, sizeof(int)),
                encryptedAccounts.Length);
            var offset = FileHeader.Length + sizeof(int);
            Buffer.BlockCopy(accountsNonce, 0, result, offset, accountsNonce.Length);
            offset += accountsNonce.Length;
            Buffer.BlockCopy(profileNonce, 0, result, offset, profileNonce.Length);
            offset += profileNonce.Length;
            Buffer.BlockCopy(encryptedAccounts, 0, result, offset, encryptedAccounts.Length);
            offset += encryptedAccounts.Length;
            Buffer.BlockCopy(encryptedProfile, 0, result, offset, encryptedProfile.Length);

            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
            CryptographicOperations.ZeroMemory(accountsNonce);
            CryptographicOperations.ZeroMemory(profileNonce);
            if (accountsData is not null) CryptographicOperations.ZeroMemory(accountsData);
            if (profileData is not null) CryptographicOperations.ZeroMemory(profileData);
            if (encryptedAccounts is not null) CryptographicOperations.ZeroMemory(encryptedAccounts);
            if (encryptedProfile is not null) CryptographicOperations.ZeroMemory(encryptedProfile);
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
        var legacyMinimumSize = LegacyFileHeader.Length + Algorithm.NonceSize + Algorithm.TagSize;
        if (encryptedBlob.Length < legacyMinimumSize)
            throw new CryptographicException("Data is too small or corrupted.");

        if (vaultKey.Length != VaultKeySize)
            throw new CryptographicException("Invalid vault key.");

        if (encryptedBlob[..LegacyFileHeader.Length].SequenceEqual(LegacyFileHeader))
            return DecryptLegacyVault(encryptedBlob, vaultKey);

        if (!encryptedBlob[..FileHeader.Length].SequenceEqual(FileHeader))
            throw new CryptographicException("Invalid file header.");

        return DecryptCurrentVault(encryptedBlob, vaultKey);
    }

    private static List<Account> DecryptLegacyVault(
        ReadOnlySpan<byte> encryptedBlob,
        ReadOnlySpan<byte> vaultKey)
    {
        var nonce = encryptedBlob.Slice(LegacyFileHeader.Length, Algorithm.NonceSize);
        var ciphertext = encryptedBlob[(LegacyFileHeader.Length + Algorithm.NonceSize)..];
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

    private static List<Account> DecryptCurrentVault(
        ReadOnlySpan<byte> encryptedBlob,
        ReadOnlySpan<byte> vaultKey)
    {
        var prefixLength = FileHeader.Length + sizeof(int) + (Algorithm.NonceSize * 2);
        if (encryptedBlob.Length < prefixLength + (Algorithm.TagSize * 2))
            throw new CryptographicException("Data is too small or corrupted.");

        var accountsLength = BinaryPrimitives.ReadInt32LittleEndian(
            encryptedBlob.Slice(FileHeader.Length, sizeof(int)));
        var availableCiphertext = encryptedBlob.Length - prefixLength;
        if (accountsLength < Algorithm.TagSize
            || accountsLength > availableCiphertext - Algorithm.TagSize)
        {
            throw new CryptographicException("Vault section length is invalid.");
        }

        var offset = FileHeader.Length + sizeof(int);
        var accountsNonce = encryptedBlob.Slice(offset, Algorithm.NonceSize);
        offset += Algorithm.NonceSize;
        var profileNonce = encryptedBlob.Slice(offset, Algorithm.NonceSize);
        offset += Algorithm.NonceSize;
        var accountsCiphertext = encryptedBlob.Slice(offset, accountsLength);
        var profileCiphertext = encryptedBlob[(offset + accountsLength)..];
        byte[]? accountsData = null;
        byte[]? profileData = null;
        try
        {
            using var key = Key.Import(Algorithm, vaultKey, KeyBlobFormat.RawSymmetricKey);
            accountsData = Algorithm.Decrypt(
                key,
                accountsNonce,
                AccountsAssociatedData,
                accountsCiphertext);
            profileData = Algorithm.Decrypt(
                key,
                profileNonce,
                ProfileAssociatedData,
                profileCiphertext);
            if (accountsData is null || profileData is null)
                throw new CryptographicException("Decryption failed.");

            var accounts = JsonSerializer.Deserialize<VaultAccountsPayload>(accountsData)
                ?? throw new JsonException("Vault account payload is null.");
            var profile = JsonSerializer.Deserialize<VaultProfilePayload>(profileData)
                ?? throw new JsonException("Vault profile payload is null.");
            if (accounts.RevisionId == Guid.Empty || accounts.RevisionId != profile.RevisionId)
                throw new CryptographicException("Vault profile revision does not match accounts.");
            return profile.Hydrate(accounts.Accounts);
        }
        finally
        {
            if (accountsData is not null) CryptographicOperations.ZeroMemory(accountsData);
            if (profileData is not null) CryptographicOperations.ZeroMemory(profileData);
        }
    }

    public VaultKeyVerificationStatus Verify(
        ReadOnlySpan<byte> encryptedVault,
        ReadOnlySpan<byte> candidateKey)
    {
        if (candidateKey.Length != VaultKeySize)
            return VaultKeyVerificationStatus.InvalidCandidateKey;

        var minimumSize = LegacyFileHeader.Length + Algorithm.NonceSize + Algorithm.TagSize;
        if (encryptedVault.Length < minimumSize
            || (!encryptedVault[..FileHeader.Length].SequenceEqual(FileHeader)
                && !encryptedVault[..LegacyFileHeader.Length].SequenceEqual(LegacyFileHeader)))
        {
            return VaultKeyVerificationStatus.InvalidVaultFormat;
        }

        try
        {
            _ = DecryptVaultWithKey(encryptedVault, candidateKey);
            return VaultKeyVerificationStatus.Verified;
        }
        catch (JsonException)
        {
            return VaultKeyVerificationStatus.InvalidVaultFormat;
        }
        catch (CryptographicException)
        {
            return VaultKeyVerificationStatus.AuthenticationFailed;
        }
    }

    internal static bool TrySplitEncryptedVault(
        ReadOnlySpan<byte> encryptedVault,
        out byte[] encryptedAccounts,
        out byte[] encryptedProfile)
    {
        encryptedAccounts = [];
        encryptedProfile = [];
        var prefixLength = FileHeader.Length + sizeof(int) + (Algorithm.NonceSize * 2);
        if (encryptedVault.Length < prefixLength + (Algorithm.TagSize * 2)
            || !encryptedVault[..FileHeader.Length].SequenceEqual(FileHeader))
        {
            return false;
        }

        var accountsCiphertextLength = BinaryPrimitives.ReadInt32LittleEndian(
            encryptedVault.Slice(FileHeader.Length, sizeof(int)));
        var availableCiphertext = encryptedVault.Length - prefixLength;
        if (accountsCiphertextLength < Algorithm.TagSize
            || accountsCiphertextLength > availableCiphertext - Algorithm.TagSize)
        {
            return false;
        }

        var offset = FileHeader.Length + sizeof(int);
        var accountsNonce = encryptedVault.Slice(offset, Algorithm.NonceSize);
        offset += Algorithm.NonceSize;
        var profileNonce = encryptedVault.Slice(offset, Algorithm.NonceSize);
        offset += Algorithm.NonceSize;
        var accountsCiphertext = encryptedVault.Slice(offset, accountsCiphertextLength);
        var profileCiphertext = encryptedVault[(offset + accountsCiphertextLength)..];

        encryptedAccounts = new byte[accountsNonce.Length + accountsCiphertext.Length];
        accountsNonce.CopyTo(encryptedAccounts);
        accountsCiphertext.CopyTo(encryptedAccounts.AsSpan(accountsNonce.Length));
        encryptedProfile = new byte[profileNonce.Length + profileCiphertext.Length];
        profileNonce.CopyTo(encryptedProfile);
        profileCiphertext.CopyTo(encryptedProfile.AsSpan(profileNonce.Length));
        return true;
    }

    internal static byte[] CombineEncryptedVault(
        ReadOnlySpan<byte> encryptedAccounts,
        ReadOnlySpan<byte> encryptedProfile)
    {
        if (encryptedAccounts.Length < Algorithm.NonceSize + Algorithm.TagSize
            || encryptedProfile.Length < Algorithm.NonceSize + Algorithm.TagSize)
        {
            throw new CryptographicException("Encrypted vault section is invalid.");
        }

        var accountsCiphertextLength = encryptedAccounts.Length - Algorithm.NonceSize;
        var profileCiphertextLength = encryptedProfile.Length - Algorithm.NonceSize;
        var prefixLength = FileHeader.Length + sizeof(int) + (Algorithm.NonceSize * 2);
        var result = new byte[
            prefixLength + accountsCiphertextLength + profileCiphertextLength];
        FileHeader.CopyTo(result, 0);
        BinaryPrimitives.WriteInt32LittleEndian(
            result.AsSpan(FileHeader.Length, sizeof(int)),
            accountsCiphertextLength);
        var offset = FileHeader.Length + sizeof(int);
        encryptedAccounts[..Algorithm.NonceSize].CopyTo(result.AsSpan(offset));
        offset += Algorithm.NonceSize;
        encryptedProfile[..Algorithm.NonceSize].CopyTo(result.AsSpan(offset));
        offset += Algorithm.NonceSize;
        encryptedAccounts[Algorithm.NonceSize..].CopyTo(result.AsSpan(offset));
        offset += accountsCiphertextLength;
        encryptedProfile[Algorithm.NonceSize..].CopyTo(result.AsSpan(offset));
        return result;
    }

    private sealed record VaultAccountPayload(
        Guid Id,
        string Issuer,
        string Secret,
        string? AccountName,
        int PeriodSeconds,
        bool IsFavorite)
    {
        public static VaultAccountPayload FromAccount(Account account) => new(
            account.ID,
            account.Issuer,
            account.Secret,
            account.AccountName,
            account.PeriodSeconds,
            account.IsFavorite);

        public Account ToAccount(IReadOnlyList<AccountGroup> groups) => new(
            Id,
            Issuer,
            Secret,
            AccountName,
            PeriodSeconds,
            isFavorite: IsFavorite,
            groups: groups);
    }

    private sealed record VaultAccountsPayload(
        Guid RevisionId,
        IReadOnlyList<VaultAccountPayload> Accounts);

    private sealed record VaultGroupMembership(
        Guid Id,
        string Name,
        string Color,
        IReadOnlyList<Guid> AccountIds);

    private sealed record VaultProfilePayload(
        int Version,
        Guid RevisionId,
        IReadOnlyList<VaultGroupMembership> Groups)
    {
        public static VaultProfilePayload FromAccounts(
            Guid revisionId,
            IReadOnlyCollection<Account> accounts)
        {
            var memberships = accounts
                .SelectMany(account => account.Groups.Select(group => (account.ID, Group: group)))
                .GroupBy(value => value.Group.Id)
                .Select(group =>
                {
                    var definition = group.First().Group;
                    return new VaultGroupMembership(
                        definition.Id,
                        definition.Name,
                        definition.Color,
                        group.Select(value => value.ID).Distinct().ToArray());
                })
                .ToArray();
            return new VaultProfilePayload(ProfileVersion, revisionId, memberships);
        }

        public List<Account> Hydrate(IReadOnlyCollection<VaultAccountPayload> accounts)
        {
            if (Version != ProfileVersion || RevisionId == Guid.Empty || Groups is null)
                throw new JsonException("Vault profile version is unsupported.");

            var knownAccountIds = accounts.Select(account => account.Id).ToHashSet();
            var memberships = new Dictionary<Guid, List<AccountGroup>>();
            var knownGroupIds = new HashSet<Guid>();
            foreach (var stored in Groups)
            {
                var group = new AccountGroup(stored.Id, stored.Name, stored.Color);
                if (!knownGroupIds.Add(group.Id)
                    || !TOTP.Core.Validation.AccountGroupPolicy.TryNormalizeStored(group, out var normalized)
                    || normalized is null)
                {
                    throw new JsonException("Vault profile group is invalid.");
                }

                foreach (var accountId in stored.AccountIds.Distinct())
                {
                    if (!knownAccountIds.Contains(accountId))
                        throw new JsonException("Vault profile references an unknown account.");
                    if (!memberships.TryGetValue(accountId, out var assigned))
                    {
                        assigned = [];
                        memberships.Add(accountId, assigned);
                    }
                    assigned.Add(normalized);
                }
            }

            return accounts
                .Select(account => account.ToAccount(
                    memberships.TryGetValue(account.Id, out var groups) ? groups : []))
                .ToList();
        }
    }
}
