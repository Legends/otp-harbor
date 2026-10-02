using TOTP.Infrastructure.Parser;

namespace TOTP.Tests.Services;

public sealed class OtpauthParserTests
{
    [Fact]
    public void NormalizeBase32SecretForUri_RemovesSeparatorsAndPadding()
    {
        var normalized = OtpauthParser.NormalizeBase32SecretForUri(" abcd-ef gh== ");

        Assert.Equal("ABCDEFGH", normalized);
    }

    [Fact]
    public void NormalizeBase32SecretForUri_WhenNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => OtpauthParser.NormalizeBase32SecretForUri(null!));
    }

    [Fact]
    public void Parse_WhenValidUriWithPathIssuer_ReturnsExpectedData()
    {
        var uri = "otpauth://totp/Acme:John%20Doe?secret=JBSWY3DPEHPK3PXP&algorithm=SHA256&digits=8&period=60";

        var parsed = OtpauthParser.Parse(uri);

        Assert.Equal("John Doe", parsed.Label);
        Assert.Equal("Acme", parsed.Issuer);
        Assert.Equal("JBSWY3DPEHPK3PXP", parsed.SecretBase32);
        Assert.Equal("SHA256", parsed.Algorithm);
        Assert.Equal(8, parsed.Digits);
        Assert.Equal(60, parsed.Period);
    }

    [Fact]
    public void Parse_WhenIssuerInQuery_OverridesPathIssuer()
    {
        var uri = "otpauth://totp/Acme:John?secret=JBSWY3DPEHPK3PXP&issuer=GitHub";

        var parsed = OtpauthParser.Parse(uri);

        Assert.Equal("John", parsed.Label);
        Assert.Equal("GitHub", parsed.Issuer);
    }

    [Fact]
    public void Parse_WhenOptionalParametersAreMissing_UsesDefaults()
    {
        var uri = "otpauth://totp/John?secret=JBSWY3DPEHPK3PXP";

        var parsed = OtpauthParser.Parse(uri);

        Assert.Equal(6, parsed.Digits);
        Assert.Equal(30, parsed.Period);
        Assert.Equal("SHA1", parsed.Algorithm);
    }

    [Theory]
    [InlineData("digits=abc")]
    [InlineData("digits=")]
    public void Parse_WhenDigitsIsMalformed_ThrowsArgumentException(string query)
    {
        var uri = $"otpauth://totp/John?secret=JBSWY3DPEHPK3PXP&{query}";

        Assert.Throws<ArgumentException>(() => OtpauthParser.Parse(uri));
    }

    [Fact]
    public void Parse_IsCaseInsensitiveAndNormalizesBase32Secret()
    {
        const string uri =
            "OTPAUTH://TOTP/Example%3Aalice%2Badmin?SECRET=jbsw-y3dp%20ehpk3pxp%3D%3D%3D%3D&ISSUER=Example&ALGORITHM=sha1&DIGITS=6&PERIOD=30";

        var parsed = OtpauthParser.Parse(uri);

        Assert.Equal("Example", parsed.Issuer);
        Assert.Equal("alice+admin", parsed.Label);
        Assert.Equal("JBSWY3DPEHPK3PXP", parsed.SecretBase32);
        Assert.Equal("sha1", parsed.Algorithm);
        Assert.Equal(6, parsed.Digits);
        Assert.Equal(30, parsed.Period);
    }

    [Fact]
    public void Parse_WhenQueryParameterIsRepeated_UsesLastValue()
    {
        const string uri =
            "otpauth://totp/Example:alice?secret=INVALID1&secret=JBSWY3DPEHPK3PXP&issuer=Old&issuer=Example";

        var parsed = OtpauthParser.Parse(uri);

        Assert.Equal("JBSWY3DPEHPK3PXP", parsed.SecretBase32);
        Assert.Equal("Example", parsed.Issuer);
    }

    [Fact]
    public void Parse_WhenPeriodIsMalformed_ThrowsArgumentException()
    {
        var uri = "otpauth://totp/John?secret=JBSWY3DPEHPK3PXP&period=xyz";

        Assert.Throws<ArgumentException>(() => OtpauthParser.Parse(uri));
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.com")]
    public void Parse_WhenUriInvalid_ThrowsArgumentException(string uri)
    {
        Assert.Throws<ArgumentException>(() => OtpauthParser.Parse(uri));
    }

    [Fact]
    public void Parse_WhenTypeIsNotTotp_ThrowsArgumentException()
    {
        var uri = "otpauth://hotp/Acme:John?secret=JBSWY3DPEHPK3PXP";

        Assert.Throws<ArgumentException>(() => OtpauthParser.Parse(uri));
    }

    [Fact]
    public void Parse_WhenSecretMissing_ThrowsArgumentException()
    {
        var uri = "otpauth://totp/Acme:John?issuer=Acme";

        Assert.Throws<ArgumentException>(() => OtpauthParser.Parse(uri));
    }

    [Fact]
    public void Parse_WhenSecretInvalid_ThrowsArgumentException()
    {
        var uri = "otpauth://totp/Acme:John?secret=ABC123";

        Assert.Throws<ArgumentException>(() => OtpauthParser.Parse(uri));
    }
}
