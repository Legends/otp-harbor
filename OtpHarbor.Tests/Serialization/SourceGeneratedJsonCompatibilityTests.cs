using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentResults;
using TOTP.Core.Backup;
using TOTP.Core.Enums;
using TOTP.Core.Models;
using TOTP.Core.Security;
using TOTP.Tests.Models;
using TOTP.Tests.Security;

namespace TOTP.Tests.Serialization;

public sealed class SourceGeneratedJsonCompatibilityTests
{
    [Fact]
    public void Preferences_PreserveExistingBytesAndEnumContract()
    {
        var options = new JsonSerializerOptions { MaxDepth = 8, WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter<AppLogLevel>(null, false));
        options.Converters.Add(new JsonStringEnumConverter<PreferredUnlockMethod>(null, false));
        AssertContract(AppPreferencesV1CodecTests.CreatePreferences(), options,
            AppPreferencesV1Codec.Serialize, AppPreferencesV1Codec.Deserialize);
    }

    [Fact]
    public void Preferences_OmittedOptionalPropertiesRetainEveryExistingDefault()
    {
        var decoded = AppPreferencesV1Codec.Deserialize("{\"format\":\"totp-preferences\",\"version\":1}"u8.ToArray());

        Assert.True(decoded.IsSuccess);
        Assert.Equal(new AppPreferencesV1(), decoded.Value);
    }

    [Fact]
    public void Authorization_PreservesExistingBytesAndReadsExistingEnvelope()
    {
        AssertContract(AuthorizationEnvelopeV2CodecTests.CreateEnvelope(),
            new JsonSerializerOptions { MaxDepth = 16, WriteIndented = true },
            AuthorizationEnvelopeV2Codec.Serialize, AuthorizationEnvelopeV2Codec.Deserialize);
    }

    [Fact]
    public void BackupConfiguration_PreservesExistingBytesIncludingOptionalHealthFields()
    {
        var configuration = new AutomaticBackupConfigurationV1
        {
            DestinationKind = "synthetic",
            DestinationLocation = "synthetic-location",
            DestinationDisplayName = "Synthetic backup",
            DestinationAccess = AutomaticBackupDestinationAccess.Unavailable,
            LastFailureKind = AutomaticBackupFailureKind.DestinationUnavailable,
            LastFailureUtc = DateTimeOffset.UnixEpoch
        };
        AssertContract(configuration, new JsonSerializerOptions { MaxDepth = 8, WriteIndented = true },
            AutomaticBackupConfigurationV1Codec.Serialize, AutomaticBackupConfigurationV1Codec.Deserialize);
    }

    [Fact]
    public void LegacyBackup_PreservesExistingBytesAndReadsExistingPackage()
    {
        var vault = new byte[] { 1, 2, 3, 4, 5, 6 };
        var package = new AutomaticBackupPackageV1
        {
            CreatedUtc = DateTimeOffset.UnixEpoch,
            EncryptedVault = vault,
            VaultSha256 = Convert.ToHexString(SHA256.HashData(vault)),
            AuthorizationEnvelope = AuthorizationEnvelopeV2CodecTests.CreateEnvelope()
        };
        AssertContract(package, new JsonSerializerOptions { MaxDepth = 24 },
            AutomaticBackupPackageV1Codec.Serialize, AutomaticBackupPackageV1Codec.Deserialize);
    }

    [Fact]
    public void CurrentBackup_ManifestPreservesExistingBytesAndPropertyOrder()
    {
        var package = new AutomaticBackupPackageV2
        {
            BackupId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            CreatedUtc = DateTimeOffset.UnixEpoch,
            EncryptedAccounts = [1, 2, 3],
            EncryptedProfile = [4, 5, 6],
            AuthorizationEnvelope = AuthorizationEnvelopeV2CodecTests.CreateEnvelope()
        };
        var encoded = AutomaticBackupPackageV2Codec.Serialize(package);
        Assert.True(encoded.IsSuccess);
        using var stream = new MemoryStream(encoded.Value);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var manifestStream = archive.GetEntry("manifest.json")!.Open();
        using var manifest = new MemoryStream();
        manifestStream.CopyTo(manifest);
        var expected = JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = AutomaticBackupPackageV2Codec.FormatIdentifier,
            version = 2,
            backupId = package.BackupId,
            createdUtc = package.CreatedUtc,
            accountsSha256 = Convert.ToHexString(SHA256.HashData(package.EncryptedAccounts)),
            profileSha256 = Convert.ToHexString(SHA256.HashData(package.EncryptedProfile)),
            authorizationSha256 = Convert.ToHexString(SHA256.HashData(
                AuthorizationEnvelopeV2Codec.Serialize(package.AuthorizationEnvelope).Value))
        });

        Assert.Equal(expected, manifest.ToArray());
    }

    private static void AssertContract<T>(T model, JsonSerializerOptions legacyOptions,
        Func<T, Result<byte[]>> encode, Func<ReadOnlyMemory<byte>, Result<T>> decode)
    {
        var legacyBytes = JsonSerializer.SerializeToUtf8Bytes(model, legacyOptions);
        var encoded = encode(model);
        Assert.True(encoded.IsSuccess);
        Assert.Equal(legacyBytes, encoded.Value);
        var decoded = decode(legacyBytes);
        Assert.True(decoded.IsSuccess);
        Assert.Equal(legacyBytes, encode(decoded.Value).Value);
    }
}
