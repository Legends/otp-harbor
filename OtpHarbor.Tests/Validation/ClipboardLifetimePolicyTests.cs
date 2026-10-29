using TOTP.Core.Validation;

namespace TOTP.Tests.Validation;

public sealed class ClipboardLifetimePolicyTests
{
    [Fact]
    public void AllowedSeconds_ContainsOnlySupportedRequiredValues()
    {
        Assert.Equal([5, 10, 15, 20, 30, 60], ClipboardLifetimePolicy.AllowedSeconds);
    }

    [Theory]
    [InlineData(-1, 5)]
    [InlineData(0, 5)]
    [InlineData(12, 10)]
    [InlineData(18, 20)]
    [InlineData(45, 30)]
    [InlineData(100, 60)]
    public void NormalizeSeconds_ReturnsNearestSupportedValue(int input, int expected)
    {
        Assert.Equal(expected, ClipboardLifetimePolicy.NormalizeSeconds(input));
    }
}
