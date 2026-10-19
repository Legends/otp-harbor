using System.IO.Compression;
using TOTP.Core.Backup;
using TOTP.Core.Security.Models;
using TOTP.Tests.Security;

namespace TOTP.Tests.Backup;

public sealed class AutomaticBackupPackageV2CodecTests
{
    [Fact]
    public void SerializeThenDeserialize_AtomicContainer_RoundTripsLinkedSections()
    {
        var package = CreatePackage();

        var encoded = AutomaticBackupPackageV2Codec.Serialize(package);
        var decoded = AutomaticBackupPackageV2Codec.Deserialize(encoded.Value);

        Assert.True(encoded.IsSuccess);
        Assert.True(AutomaticBackupPackageV2Codec.IsContainer(encoded.Value));
        Assert.True(decoded.IsSuccess);
        Assert.Equal(package.BackupId, decoded.Value.BackupId);
        Assert.Equal(package.CreatedUtc, decoded.Value.CreatedUtc);
        Assert.Equal(package.EncryptedAccounts, decoded.Value.EncryptedAccounts);
        Assert.Equal(package.EncryptedProfile, decoded.Value.EncryptedProfile);
        Assert.Null(decoded.Value.AuthorizationEnvelope.QuickUnlockWrapper);

        using var stream = new MemoryStream(encoded.Value, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.Equal(
            ["authorization.json", "manifest.json", "profile.bin", "vault.bin"],
            archive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Deserialize_ProfileChangedWithoutManifestUpdate_FailsClosed()
    {
        var encoded = AutomaticBackupPackageV2Codec.Serialize(CreatePackage()).Value;
        var tampered = RewriteEntry(encoded, "profile.bin", [9, 9, 9, 9]);

        var result = AutomaticBackupPackageV2Codec.Deserialize(tampered);

        Assert.True(result.IsFailed);
        Assert.Equal(AutomaticBackupErrorCode.InvalidFormat, ErrorCode(result.Errors));
    }

    [Fact]
    public void Deserialize_UnexpectedEntry_FailsClosed()
    {
        var encoded = AutomaticBackupPackageV2Codec.Serialize(CreatePackage()).Value;
        using var stream = new MemoryStream();
        stream.Write(encoded);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            using var entry = archive.CreateEntry("untrusted.bin").Open();
            entry.WriteByte(1);
        }

        var result = AutomaticBackupPackageV2Codec.Deserialize(stream.ToArray());

        Assert.True(result.IsFailed);
        Assert.Equal(AutomaticBackupErrorCode.InvalidFormat, ErrorCode(result.Errors));
    }

    [Fact]
    public void Serialize_DeviceSpecificQuickUnlockWrapper_FailsClosed()
    {
        var package = CreatePackage() with
        {
            AuthorizationEnvelope = AuthorizationEnvelopeV2CodecTests.CreateEnvelope() with
            {
                QuickUnlockWrapper = new PlatformQuickUnlockWrapperV2
                {
                    Provider = "test-device",
                    ProviderVersion = 1,
                    AuthenticationPolicy = "user-verification-required",
                    KeyReference = "device-only-reference",
                    WrappedKey = new PlatformWrappedKeyV2
                    {
                        Algorithm = "test",
                        Ciphertext = new byte[32]
                    }
                }
            }
        };

        var result = AutomaticBackupPackageV2Codec.Serialize(package);

        Assert.True(result.IsFailed);
        Assert.Equal(AutomaticBackupErrorCode.InvalidFormat, ErrorCode(result.Errors));
    }

    private static AutomaticBackupPackageV2 CreatePackage() => new()
    {
        BackupId = Guid.NewGuid(),
        CreatedUtc = new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero),
        EncryptedAccounts = [1, 2, 3, 4],
        EncryptedProfile = [5, 6, 7, 8],
        AuthorizationEnvelope = AuthorizationEnvelopeV2CodecTests.CreateEnvelope()
    };

    private static byte[] RewriteEntry(byte[] encoded, string entryName, byte[] replacement)
    {
        using var stream = new MemoryStream();
        stream.Write(encoded);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry(entryName)!.Delete();
            using var entry = archive.CreateEntry(entryName).Open();
            entry.Write(replacement);
        }

        return stream.ToArray();
    }

    private static AutomaticBackupErrorCode ErrorCode(IEnumerable<FluentResults.IError> errors) =>
        Assert.IsType<AutomaticBackupError>(Assert.Single(errors)).Code;
}
