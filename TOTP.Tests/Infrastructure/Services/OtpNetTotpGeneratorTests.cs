using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Infrastructure.Services;

public sealed class OtpNetTotpGeneratorTests
{
    [Fact]
    public void Generate_WithValidBase32Secret_ReturnsSixDigitCodeAndValidTiming()
    {
        var sut = new OtpNetTotpGenerator();

        var result = sut.Generate("JBSWY3DPEHPK3PXP");

        Assert.Matches("^[0-9]{6}$", result.Code);
        Assert.Equal(30, result.PeriodSeconds);
        Assert.InRange(result.RemainingSeconds, 1, result.PeriodSeconds);
    }

    [Fact]
    public void Generate_WithInvalidSecret_ThrowsFormatException()
    {
        var sut = new OtpNetTotpGenerator();

        Assert.Throws<FormatException>(() => sut.Generate("not valid base32!"));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(60)]
    [InlineData(600)]
    [InlineData(3600)]
    public void Generate_WithSupportedCustomPeriod_ReturnsMatchingTiming(int periodSeconds)
    {
        var sut = new OtpNetTotpGenerator();

        var result = sut.Generate("JBSWY3DPEHPK3PXP", periodSeconds);

        Assert.Equal(periodSeconds, result.PeriodSeconds);
        Assert.InRange(result.RemainingSeconds, 1, periodSeconds);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(3601)]
    public void Generate_WithUnsupportedPeriod_ThrowsArgumentOutOfRangeException(int periodSeconds)
    {
        var sut = new OtpNetTotpGenerator();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            sut.Generate("JBSWY3DPEHPK3PXP", periodSeconds));
    }
}
