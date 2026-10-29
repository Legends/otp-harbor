using System.Security.Cryptography;
using System.Text;
using TOTP.Core.Backup;
using TOTP.Core.Security.Models;
using TOTP.Tests.Security;

namespace TOTP.Tests.Backup;

public sealed class AutomaticBackupPackageV1CodecTests
{
    [Fact]
    public void SerializeThenDeserialize_PortablePackage_RoundTrips()
    {
        var package = CreatePackage();

        var encoded = AutomaticBackupPackageV1Codec.Serialize(package);
        var decoded = AutomaticBackupPackageV1Codec.Deserialize(encoded.Value);

        Assert.True(encoded.IsSuccess);
        Assert.True(decoded.IsSuccess);
        Assert.Equal(package.EncryptedVault, decoded.Value.EncryptedVault);
        Assert.Null(decoded.Value.AuthorizationEnvelope.QuickUnlockWrapper);
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

        var result = AutomaticBackupPackageV1Codec.Serialize(package);

        Assert.True(result.IsFailed);
        Assert.Equal(AutomaticBackupErrorCode.InvalidFormat, ErrorCode(result.Errors));
    }

    [Fact]
    public void Deserialize_TamperedVault_FailsIntegrityValidation()
    {
        var encoded = AutomaticBackupPackageV1Codec.Serialize(CreatePackage()).Value;
        var json = Encoding.UTF8.GetString(encoded)
            .Replace("AQIDBAUG", "AQIDBAUH", StringComparison.Ordinal);

        var result = AutomaticBackupPackageV1Codec.Deserialize(Encoding.UTF8.GetBytes(json));

        Assert.True(result.IsFailed);
        Assert.Equal(AutomaticBackupErrorCode.InvalidVault, ErrorCode(result.Errors));
    }

    [Fact]
    public void Deserialize_DuplicateProperty_FailsClosed()
    {
        var encoded = AutomaticBackupPackageV1Codec.Serialize(CreatePackage()).Value;
        var serialized = Encoding.UTF8.GetString(encoded);
        var json = "{\"format\":\"ambiguous\"," + serialized[1..];

        var result = AutomaticBackupPackageV1Codec.Deserialize(Encoding.UTF8.GetBytes(json));

        Assert.True(result.IsFailed);
        Assert.Equal(AutomaticBackupErrorCode.InvalidFormat, ErrorCode(result.Errors));
    }

    private static AutomaticBackupPackageV1 CreatePackage()
    {
        var vault = new byte[] { 1, 2, 3, 4, 5, 6 };
        return new AutomaticBackupPackageV1
        {
            CreatedUtc = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero),
            VaultSha256 = Convert.ToHexString(SHA256.HashData(vault)),
            EncryptedVault = vault,
            AuthorizationEnvelope = AuthorizationEnvelopeV2CodecTests.CreateEnvelope()
        };
    }

    private static AutomaticBackupErrorCode ErrorCode(IEnumerable<FluentResults.IError> errors) =>
        Assert.IsType<AutomaticBackupError>(Assert.Single(errors)).Code;
}
