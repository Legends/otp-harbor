using TOTP.Core.Validation;

namespace TOTP.Tests.Validation;

public sealed class SecretValidationTests
{
    [Theory]
    [InlineData("ORSXG5A")]
    [InlineData("JBSW Y3DP-EHPK 3PXP")]
    [InlineData("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ")]
    public void IsValidBase32Secret_AcceptsCanonicalCompatibleSecrets(string secret)
    {
        Assert.True(SecretValidation.IsValidBase32Secret(secret));
    }

    [Fact]
    public void IsValidBase32Secret_RejectsCharactersOutsideTheAlphabet()
    {
        Assert.False(SecretValidation.IsValidBase32Secret("not-valid-*"));
    }

    [Theory]
    [InlineData("TEST")]
    [InlineData("ASDF")]
    public void IsValidBase32Secret_PreservesLegacyDecoderCompatibility(string secret)
    {
        Assert.True(SecretValidation.IsValidBase32Secret(secret));
    }

    [Theory]
    [InlineData("not-valid-*")]
    [InlineData("TEST")]
    [InlineData("ASDF")]
    [InlineData("FOO")]
    [InlineData("ANYTHING")]
    [InlineData("ORSXG5A")]
    [InlineData("JBSWY3DPEHPK3PXP")]
    public void IsValidNewAccountBase32Secret_RejectsInvalidNonCanonicalOrShortSecrets(
        string secret)
    {
        Assert.False(SecretValidation.IsValidNewAccountBase32Secret(secret));
    }

    [Theory]
    [InlineData("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ")]
    [InlineData("GEZD GNBV GY3T-QOJQ GEZD GNBV GY3T QOJQ====")]
    public void IsValidNewAccountBase32Secret_AcceptsCanonical128BitOrLongerSecrets(string secret)
    {
        Assert.True(SecretValidation.IsValidNewAccountBase32Secret(secret));
    }
}
