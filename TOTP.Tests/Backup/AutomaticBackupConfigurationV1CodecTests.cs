using System.Text;
using TOTP.Core.Backup;

namespace TOTP.Tests.Backup;

public sealed class AutomaticBackupConfigurationV1CodecTests
{
    [Fact]
    public void SerializeThenDeserialize_ValidConfiguration_RoundTrips()
    {
        var configuration = CreateConfiguration();

        var encoded = AutomaticBackupConfigurationV1Codec.Serialize(configuration);
        var decoded = AutomaticBackupConfigurationV1Codec.Deserialize(encoded.Value);

        Assert.True(encoded.IsSuccess);
        Assert.True(decoded.IsSuccess);
        Assert.Equal(configuration, decoded.Value);
    }

    [Fact]
    public void Deserialize_DuplicateDestinationLocation_FailsClosed()
    {
        var encoded = AutomaticBackupConfigurationV1Codec.Serialize(CreateConfiguration()).Value;
        var serialized = Encoding.UTF8.GetString(encoded);
        var json = "{\"destinationLocation\":\"ambiguous\"," + serialized[1..];

        var result = AutomaticBackupConfigurationV1Codec.Deserialize(Encoding.UTF8.GetBytes(json));

        Assert.True(result.IsFailed);
        Assert.Equal(
            AutomaticBackupErrorCode.InvalidFormat,
            Assert.IsType<AutomaticBackupError>(Assert.Single(result.Errors)).Code);
    }

    [Fact]
    public void Deserialize_FailureKindWithoutTimestamp_FailsClosed()
    {
        var configuration = CreateConfiguration() with
        {
            LastFailureKind = AutomaticBackupFailureKind.WriteFailed,
            LastFailureUtc = null
        };
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(configuration);

        var result = AutomaticBackupConfigurationV1Codec.Deserialize(json);

        Assert.True(result.IsFailed);
        Assert.Equal(
            AutomaticBackupErrorCode.InvalidFormat,
            Assert.IsType<AutomaticBackupError>(Assert.Single(result.Errors)).Code);
    }

    private static AutomaticBackupConfigurationV1 CreateConfiguration() => new()
    {
        DestinationKind = "desktop-folder-v1",
        DestinationLocation = "C:\\Backups",
        DestinationDisplayName = "Backups",
        LastFingerprint = new string('A', 64),
        LastSuccessfulUtc = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero),
        LastSuccessfulFileName = "otp-harbor-auto-current.otphbackup",
        DestinationAccess = AutomaticBackupDestinationAccess.Unavailable,
        LastFailureKind = AutomaticBackupFailureKind.DestinationUnavailable,
        LastFailureUtc = new DateTimeOffset(2026, 10, 17, 12, 5, 0, TimeSpan.Zero)
    };
}
