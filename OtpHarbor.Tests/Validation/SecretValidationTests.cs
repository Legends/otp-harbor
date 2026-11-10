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
    public void AllSecretValidationPaths_PreserveDecoderCompatibility(string secret)
    {
        Assert.True(SecretValidation.IsValidStoredBase32Secret(secret));
        Assert.True(SecretValidation.IsValidBase32Secret(secret));
        Assert.True(SecretValidation.IsValidNewAccountBase32Secret(secret));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("A")]
    [InlineData("====")]
    [InlineData("not-valid-*")]
    [InlineData("ABC0189")]
    [InlineData("AB=CD")]
    public void IsValidNewAccountBase32Secret_RejectsEmptyOrUndecodableSecrets(
        string? secret)
    {
        Assert.False(SecretValidation.IsValidNewAccountBase32Secret(secret));
    }

    [Theory]
    [InlineData("AA")]
    [InlineData("ANYTHING")]
    [InlineData("ORSXG5A")]
    [InlineData("JBSWY3DPEHPK3PXP")]
    [InlineData("jbsw y3dp-ehpk 3pxp====")]
    [InlineData("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ")]
    [InlineData("GEZD GNBV GY3T-QOJQ GEZD GNBV GY3T QOJQ====")]
    public void IsValidNewAccountBase32Secret_AcceptsCanonicalSecretsRegardlessOfBitLength(string secret)
    {
        Assert.True(SecretValidation.IsValidNewAccountBase32Secret(secret));
        Assert.True(SecretValidation.IsValidBase32Secret(secret));
    }
}
