using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentResults;
using TOTP.Core.Security;
using TOTP.Core.Security.Models;

namespace TOTP.Core.Backup;

public static class AutomaticBackupPackageV2Codec
{
    public const string FormatIdentifier = "otp-harbor-automatic-backup";
    public const int CurrentVersion = 2;
    public const int MaximumAccountsSize = 16 * 1024 * 1024;
    public const int MaximumProfileSize = 4 * 1024 * 1024;
    public const int MaximumPayloadSize = 24 * 1024 * 1024;

    private const string ManifestEntryName = "manifest.json";
    private const string AccountsEntryName = "vault.bin";
    private const string ProfileEntryName = "profile.bin";
    private const string AuthorizationEntryName = "authorization.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        MaxDepth = 24,
        WriteIndented = false
    };

    public static bool IsContainer(ReadOnlySpan<byte> payload) =>
        payload.Length >= 4
        && payload[0] == (byte)'P'
        && payload[1] == (byte)'K'
        && payload[2] == 3
        && payload[3] == 4;

    public static Result<byte[]> Serialize(AutomaticBackupPackageV2 package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.BackupId == Guid.Empty
            || package.CreatedUtc == default
            || package.EncryptedAccounts is not { Length: > 0 }
            || package.EncryptedAccounts.Length > MaximumAccountsSize
            || package.EncryptedProfile is not { Length: > 0 }
            || package.EncryptedProfile.Length > MaximumProfileSize
            || package.AuthorizationEnvelope is null
            || package.AuthorizationEnvelope.QuickUnlockWrapper is not null)
        {
            return Fail<byte[]>(AutomaticBackupErrorCode.InvalidFormat, "Automatic backup package is invalid.");
        }

        byte[]? envelope = null;
        byte[]? manifest = null;
        try
        {
            var encodedEnvelope = AuthorizationEnvelopeV2Codec.Serialize(package.AuthorizationEnvelope);
            if (encodedEnvelope.IsFailed)
                return Fail<byte[]>(AutomaticBackupErrorCode.InvalidFormat, "Automatic backup authorization envelope is invalid.");
            envelope = encodedEnvelope.Value;

            manifest = JsonSerializer.SerializeToUtf8Bytes(new Manifest
            {
                BackupId = package.BackupId,
                CreatedUtc = package.CreatedUtc,
                AccountsSha256 = ComputeSha256(package.EncryptedAccounts),
                ProfileSha256 = ComputeSha256(package.EncryptedProfile),
                AuthorizationSha256 = ComputeSha256(envelope)
            }, JsonOptions);

            using var output = new MemoryStream();
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                WriteEntry(archive, ManifestEntryName, manifest);
                WriteEntry(archive, AccountsEntryName, package.EncryptedAccounts);
                WriteEntry(archive, ProfileEntryName, package.EncryptedProfile);
                WriteEntry(archive, AuthorizationEntryName, envelope);
            }

            if (output.Length > MaximumPayloadSize)
                return Fail<byte[]>(AutomaticBackupErrorCode.TooLarge, "Automatic backup exceeds the size limit.");
            return Result.Ok(output.ToArray());
        }
        finally
        {
            if (envelope is not null) CryptographicOperations.ZeroMemory(envelope);
            if (manifest is not null) CryptographicOperations.ZeroMemory(manifest);
        }
    }

    public static Result<AutomaticBackupPackageV2> Deserialize(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty || payload.Length > MaximumPayloadSize || !IsContainer(payload.Span))
            return Fail<AutomaticBackupPackageV2>(AutomaticBackupErrorCode.InvalidFormat, "Automatic backup container is invalid.");

        byte[]? manifestData = null;
        byte[]? accounts = null;
        byte[]? profile = null;
        byte[]? envelopeData = null;
        try
        {
            using var input = new MemoryStream(payload.ToArray(), writable: false);
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
            var entries = archive.Entries.ToDictionary(entry => entry.FullName, StringComparer.Ordinal);
            if (entries.Count != 4
                || entries.Count != archive.Entries.Count
                || !entries.TryGetValue(ManifestEntryName, out var manifestEntry)
                || !entries.TryGetValue(AccountsEntryName, out var accountsEntry)
                || !entries.TryGetValue(ProfileEntryName, out var profileEntry)
                || !entries.TryGetValue(AuthorizationEntryName, out var authorizationEntry))
            {
                return Fail<AutomaticBackupPackageV2>(AutomaticBackupErrorCode.InvalidFormat, "Automatic backup entries are invalid.");
            }

            manifestData = ReadEntry(manifestEntry, 64 * 1024);
            accounts = ReadEntry(accountsEntry, MaximumAccountsSize);
            profile = ReadEntry(profileEntry, MaximumProfileSize);
            envelopeData = ReadEntry(authorizationEntry, 256 * 1024);
            if (HasDuplicateProperty(manifestData))
                return Fail<AutomaticBackupPackageV2>(AutomaticBackupErrorCode.InvalidFormat, "Automatic backup manifest contains duplicate properties.");

            var manifest = JsonSerializer.Deserialize<Manifest>(manifestData, JsonOptions);
            if (manifest is null
                || manifest.Format != FormatIdentifier
                || manifest.Version != CurrentVersion
                || manifest.BackupId == Guid.Empty
                || manifest.CreatedUtc == default
                || !MatchesHash(accounts, manifest.AccountsSha256)
                || !MatchesHash(profile, manifest.ProfileSha256)
                || !MatchesHash(envelopeData, manifest.AuthorizationSha256))
            {
                return Fail<AutomaticBackupPackageV2>(AutomaticBackupErrorCode.InvalidFormat, "Automatic backup manifest validation failed.");
            }

            var envelope = AuthorizationEnvelopeV2Codec.Deserialize(envelopeData);
            if (envelope.IsFailed || envelope.Value.QuickUnlockWrapper is not null)
                return Fail<AutomaticBackupPackageV2>(AutomaticBackupErrorCode.InvalidFormat, "Automatic backup authorization envelope is invalid.");

            var result = new AutomaticBackupPackageV2
            {
                BackupId = manifest.BackupId,
                CreatedUtc = manifest.CreatedUtc,
                EncryptedAccounts = accounts,
                EncryptedProfile = profile,
                AuthorizationEnvelope = envelope.Value
            };
            accounts = null;
            profile = null;
            return Result.Ok(result);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or JsonException or ArgumentException)
        {
            return Fail<AutomaticBackupPackageV2>(AutomaticBackupErrorCode.InvalidFormat, "Automatic backup container could not be read.");
        }
        finally
        {
            if (manifestData is not null) CryptographicOperations.ZeroMemory(manifestData);
            if (accounts is not null) CryptographicOperations.ZeroMemory(accounts);
            if (profile is not null) CryptographicOperations.ZeroMemory(profile);
            if (envelopeData is not null) CryptographicOperations.ZeroMemory(envelopeData);
        }
    }

    public static string ComputeFingerprint(
        ReadOnlySpan<byte> encryptedAccounts,
        ReadOnlySpan<byte> encryptedProfile,
        ReadOnlySpan<byte> encodedPortableEnvelope)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(encryptedAccounts);
        hash.AppendData(encryptedProfile);
        hash.AppendData(encodedPortableEnvelope);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void WriteEntry(ZipArchive archive, string name, ReadOnlySpan<byte> data)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(data);
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry, int maximumBytes)
    {
        if (entry.Length <= 0 || entry.Length > maximumBytes)
            throw new InvalidDataException("Automatic backup entry size is invalid.");
        using var source = entry.Open();
        using var destination = new MemoryStream((int)entry.Length);
        var buffer = new byte[81920];
        try
        {
            while (true)
            {
                var read = source.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                if (destination.Length + read > maximumBytes)
                    throw new InvalidDataException("Automatic backup entry exceeds the size limit.");
                destination.Write(buffer, 0, read);
            }
            return destination.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static string ComputeSha256(ReadOnlySpan<byte> payload) =>
        Convert.ToHexString(SHA256.HashData(payload));

    private static bool MatchesHash(ReadOnlySpan<byte> payload, string expectedText)
    {
        byte[]? expected = null;
        var actual = SHA256.HashData(payload);
        try
        {
            if (expectedText is not { Length: 64 }) return false;
            expected = Convert.FromHexString(expectedText);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
        finally
        {
            if (expected is not null) CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(actual);
        }
    }

    private static bool HasDuplicateProperty(ReadOnlyMemory<byte> payload)
    {
        using var document = JsonDocument.Parse(payload);
        return HasDuplicateProperty(document.RootElement);
    }

    private static bool HasDuplicateProperty(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || HasDuplicateProperty(property.Value)) return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(HasDuplicateProperty);
        }
        return false;
    }

    private static Result<T> Fail<T>(AutomaticBackupErrorCode code, string message) =>
        Result.Fail<T>(new AutomaticBackupError(code, message));

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record Manifest
    {
        [JsonRequired]
        [JsonPropertyName("format")]
        public string Format { get; init; } = FormatIdentifier;

        [JsonRequired]
        [JsonPropertyName("version")]
        public int Version { get; init; } = CurrentVersion;

        [JsonRequired]
        [JsonPropertyName("backupId")]
        public required Guid BackupId { get; init; }

        [JsonRequired]
        [JsonPropertyName("createdUtc")]
        public required DateTimeOffset CreatedUtc { get; init; }

        [JsonRequired]
        [JsonPropertyName("accountsSha256")]
        public required string AccountsSha256 { get; init; }

        [JsonRequired]
        [JsonPropertyName("profileSha256")]
        public required string ProfileSha256 { get; init; }

        [JsonRequired]
        [JsonPropertyName("authorizationSha256")]
        public required string AuthorizationSha256 { get; init; }
    }
}
