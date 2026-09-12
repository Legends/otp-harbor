using TOTP.Infrastructure.Services;
using TOTP.Core.Services.Interfaces;
using TOTP.Tests.TestData;

namespace TOTP.Tests.Services;

public sealed class QrPayloadValidatorTests
{
    private readonly QrPayloadValidator _sut = new();

    [Fact]
    public void Validate_WhenTotpPayloadIsValid_ReturnsOnlySafeDescriptor()
    {
        var result = _sut.Validate(
            "otpauth://totp/Example:alice?secret=JBSWY3DPEHPK3PXP&issuer=Example");

        Assert.True(result.IsValid);
        Assert.Equal("Example", result.Issuer);
        Assert.Equal("alice", result.AccountName);
        Assert.DoesNotContain("JBSWY", result.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.invalid/not-otp")]
    [InlineData("otpauth://totp/demo?secret=not*base32")]
    public void Validate_WhenPayloadIsUntrusted_ReturnsInvalidWithoutThrowing(string payload)
    {
        var result = _sut.Validate(payload);

        Assert.False(result.IsValid);
        Assert.Empty(result.Issuer);
        Assert.Empty(result.AccountName);
    }

    [Fact]
    public void Validate_WhenPayloadExceedsLimit_FailsClosed()
    {
        var result = _sut.Validate("otpauth://totp/" + new string('a', 4096));

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("SHA256", 6, 30)]
    [InlineData("SHA1", 8, 30)]
    [InlineData("SHA1", 6, 4)]
    [InlineData("SHA1", 6, 3601)]
    public void Validate_WhenTotpParametersCannotBePersisted_FailsClosed(
        string algorithm,
        int digits,
        int period)
    {
        var result = _sut.Validate(
            $"otpauth://totp/Example:alice?secret=JBSWY3DPEHPK3PXP&algorithm={algorithm}&digits={digits}&period={period}");

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(60)]
    [InlineData(600)]
    [InlineData(3600)]
    public void Validate_WhenTotpPeriodIsSupported_ReturnsValidDescriptor(int period)
    {
        var result = _sut.Validate(
            $"otpauth://totp/Example:alice?secret=JBSWY3DPEHPK3PXP&period={period}");

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WhenGoogleAuthenticatorMigrationPayloadIsValid_ReturnsSafeDescriptor()
    {
        const string payload =
            "otpauth-migration://offline?data=CioKClRlc3RTZWNyZXQSDUV4YW1wbGU6YWxpY2UaB0V4YW1wbGUgASgBMAIQARgBIAAoKg%3D%3D";

        var result = _sut.Validate(payload);

        Assert.True(result.IsValid);
        Assert.Equal(QrPayloadKind.GoogleAuthenticatorMigration, result.Kind);
        Assert.Equal(1, result.AccountCount);
        Assert.DoesNotContain("TestSecret", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_WhenGoogleAuthenticatorMigrationContainsTenAccountsAndShortSecrets_ReturnsValid()
    {
        var result = _sut.Validate(GoogleAuthenticatorMigrationTestData.TenAccountPayload);

        Assert.True(result.IsValid);
        Assert.Equal(QrPayloadKind.GoogleAuthenticatorMigration, result.Kind);
        Assert.Equal(10, result.AccountCount);
    }

    [Theory]
    [InlineData("otpauth-migration://offline?data=")]
    [InlineData("otpauth-migration://offline?data=not-base64")]
    [InlineData("otpauth-migration://offline?data=Cg%3D%3D")]
    [InlineData("otpauth-migration://offline?data=CioKClRlc3RTZWNyZXQSDUV4YW1wbGU6YWxpY2UaB0V4YW1wbGUgASgBMAIQAhgBIAAoKg%3D%3D")]
    public void Validate_WhenGoogleAuthenticatorMigrationPayloadIsMalformedOrUnsupported_FailsClosed(
        string payload)
    {
        var result = _sut.Validate(payload);

        Assert.False(result.IsValid);
        Assert.Empty(result.Issuer);
        Assert.Empty(result.AccountName);
    }
}
