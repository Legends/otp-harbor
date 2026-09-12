using System.Text;
using TOTP.Core.Enums;
using TOTP.Core.Models;

namespace TOTP.Tests.Models;

public sealed class AppPreferencesV1CodecTests
{
    [Fact]
    public void SerializeThenDeserialize_RoundTripsEveryPreference()
    {
        var preferences = CreatePreferences();

        var encoded = AppPreferencesV1Codec.Serialize(preferences);
        var decoded = AppPreferencesV1Codec.Deserialize(encoded.Value);

        Assert.True(encoded.IsSuccess);
        Assert.True(decoded.IsSuccess);
        Assert.Equal(preferences, decoded.Value);
    }

    [Fact]
    public void Serialize_NeverEmitsAuthorizationOrKeyMaterialFields()
    {
        var encoded = AppPreferencesV1Codec.Serialize(CreatePreferences());
        var json = Encoding.UTF8.GetString(encoded.Value);

        Assert.DoesNotContain("authorization", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordWrappedDek", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("helloWrappedDek", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordSalt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("otpSeed", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"preferredUnlockMethod\": \"PlatformQuickUnlock\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Hello", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("{\"format\":\"totp-preferences\",\"format\":\"other\",\"version\":1}")]
    [InlineData("{\"format\":\"totp-preferences\",\"version\":1,\"authorization\":{}}")]
    [InlineData("{\"format\":\"totp-preferences\",\"version\":1,\"minimumLogLevel\":2}")]
    [InlineData("{\"format\":\"totp-preferences\",\"version\":1,\"preferredUnlockMethod\":1}")]
    [InlineData("{\"format\":\"totp-preferences\",\"version\":1,\"preferredUnlockMethod\":\"Unknown\"}")]
    public void Deserialize_MalformedDuplicateOrAuthorizationField_FailsClosed(string json)
    {
        var result = AppPreferencesV1Codec.Deserialize(Encoding.UTF8.GetBytes(json));

        Assert.False(result.IsSuccess);
        Assert.Equal(AppPreferencesErrorCode.Malformed, ErrorCode(result.Errors));
    }

    [Theory]
    [InlineData(-1, 15, 1.5)]
    [InlineData(1441, 15, 1.5)]
    [InlineData(10, 0, 1.5)]
    [InlineData(10, 301, 1.5)]
    [InlineData(10, 15, 1.25)]
    [InlineData(10, 15, 6.5)]
    public void Serialize_InvalidValue_ReturnsTypedFailure(int idleMinutes, int clearSeconds, double qrScale)
    {
        var preferences = CreatePreferences() with
        {
            IdleTimeoutMinutes = idleMinutes,
            ClearClipboardSeconds = clearSeconds,
            QrPreviewScaleFactor = qrScale
        };

        var result = AppPreferencesV1Codec.Serialize(preferences);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppPreferencesErrorCode.InvalidValue, ErrorCode(result.Errors));
    }

    [Fact]
    public void Serialize_InvalidPreferredUnlockMethod_ReturnsTypedFailure()
    {
        var preferences = CreatePreferences() with
        {
            PreferredUnlockMethod = (PreferredUnlockMethod)999
        };

        var result = AppPreferencesV1Codec.Serialize(preferences);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppPreferencesErrorCode.InvalidValue, ErrorCode(result.Errors));
    }

    [Fact]
    public void SerializeThenDeserialize_DeviceCredentialPreference_RoundTrips()
    {
        var preferences = CreatePreferences() with
        {
            PreferredUnlockMethod = PreferredUnlockMethod.PlatformDeviceCredential
        };

        var encoded = AppPreferencesV1Codec.Serialize(preferences);
        var decoded = AppPreferencesV1Codec.Deserialize(encoded.Value);

        Assert.True(encoded.IsSuccess);
        Assert.True(decoded.IsSuccess);
        Assert.Equal(
            PreferredUnlockMethod.PlatformDeviceCredential,
            decoded.Value.PreferredUnlockMethod);
    }

    [Theory]
    [InlineData(-25)]
    [InlineData(25)]
    [InlineData(110)]
    [InlineData(325)]
    public void Serialize_InvalidInterfaceScale_ReturnsTypedFailure(int percent)
    {
        var result = AppPreferencesV1Codec.Serialize(CreatePreferences() with
        {
            InterfaceScalePercent = percent
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(AppPreferencesErrorCode.InvalidValue, ErrorCode(result.Errors));
    }

    [Fact]
    public void Deserialize_WhenPreferredUnlockMethodIsMissing_DefaultsToPassword()
    {
        const string json = "{\"format\":\"totp-preferences\",\"version\":1}";

        var result = AppPreferencesV1Codec.Deserialize(Encoding.UTF8.GetBytes(json));

        Assert.True(result.IsSuccess);
        Assert.Equal(PreferredUnlockMethod.Password, result.Value.PreferredUnlockMethod);
        Assert.True(result.Value.AppLockEnabled);
    }

    internal static AppPreferencesV1 CreatePreferences() => new()
    {
        CultureName = "de-DE",
        MinimumLogLevel = AppLogLevel.Warning,
        PreferredUnlockMethod = PreferredUnlockMethod.PlatformQuickUnlock,
        AppLockEnabled = false,
        IdleTimeoutMinutes = 7,
        LockOnSessionLock = false,
        LockOnMinimize = false,
        ClearClipboardEnabled = true,
        ClearClipboardSeconds = 12,
        QrPreviewScaleFactor = 2.5,
        InterfaceScalePercent = 175,
        ExportEncrypt = false,
        OpenExportFileAfterExport = false,
        HideSecretsByDefault = false
    };

    internal static AppPreferencesErrorCode ErrorCode(IEnumerable<FluentResults.IError> errors) =>
        Assert.IsType<AppPreferencesError>(Assert.Single(errors)).Code;
}
