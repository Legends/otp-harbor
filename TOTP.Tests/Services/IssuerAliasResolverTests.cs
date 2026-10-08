using System.Text;
using TOTP.Infrastructure.Branding;

namespace TOTP.Tests.Services;

public sealed class IssuerAliasResolverTests
{
    [Fact]
    public void LoadDefault_ProvidesVersionedLogoFreeAliases()
    {
        var sut = IssuerAliasResolver.LoadDefault();

        Assert.True(sut.AliasCount > 20);
        Assert.True(sut.TryResolve(IssuerAliasResolver.Normalize("  GOOGLE-WORKSPACE "), out var google));
        Assert.Equal("google", google);
        Assert.True(sut.TryResolve(IssuerAliasResolver.Normalize("Bitbucket.org"), out var bitbucket));
        Assert.Equal("bitbucket", bitbucket);
        Assert.False(sut.TryResolve(IssuerAliasResolver.Normalize("My Private Server"), out _));
    }

    [Fact]
    public void Load_RejectsAliasesThatNormalizeToDifferentBrands()
    {
        using var stream = JsonStream("""
            {
              "schemaVersion": 1,
              "entries": [
                { "brandId": "alpha", "aliases": ["Shared.Login"] },
                { "brandId": "beta", "aliases": ["Shared Login"] }
              ]
            }
            """);

        Assert.Throws<InvalidDataException>(() => IssuerAliasResolver.Load(stream));
    }

    [Fact]
    public void Load_RejectsUnsupportedSchemaVersion()
    {
        using var stream = JsonStream("""
            {
              "schemaVersion": 2,
              "entries": [
                { "brandId": "alpha", "aliases": ["Alpha Account"] }
              ]
            }
            """);

        Assert.Throws<InvalidDataException>(() => IssuerAliasResolver.Load(stream));
    }

    [Theory]
    [InlineData("  C++ / R&D  ", "c++ r&d")]
    [InlineData("ＧｉｔＨｕｂ", "github")]
    [InlineData("Example---Cloud", "example cloud")]
    [InlineData("AT&T", "at&t")]
    public void OtpHarborFormatV1Normalization_MatchesBuilderContract(
        string issuer,
        string expected)
    {
        Assert.Equal(expected, OtpHarborIssuerNormalizer.Normalize(issuer));
    }

    private static MemoryStream JsonStream(string json) =>
        new(Encoding.UTF8.GetBytes(json));
}
