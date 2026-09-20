using TOTP.Avalonia.Shared.Controls;

namespace TOTP.Tests.Avalonia.Shared;

public sealed class AccountRowTests
{
    [Fact]
    public void Metadata_ProducesOneReadableAccessibleName()
    {
        var sut = new AccountRow
        {
            Issuer = "Example",
            AccountName = "alice@example.test"
        };

        Assert.Equal("Example, alice@example.test", sut.AccessibleName);
        Assert.Equal("Example", sut.PrimaryText);
        Assert.Equal("alice@example.test", sut.SecondaryText);
        Assert.True(sut.HasSecondaryText);
    }

    [Theory]
    [InlineData("Example", "", "Example")]
    [InlineData("", "alice", "alice")]
    [InlineData("  ", "  ", "Account")]
    public void PartialMetadata_RemainsMeaningful(
        string issuer,
        string accountName,
        string expected)
    {
        var sut = new AccountRow
        {
            Issuer = issuer,
            AccountName = accountName
        };

        Assert.Equal(expected, sut.AccessibleName);
        Assert.Equal(expected == "Account" ? string.Empty : expected, sut.PrimaryText);
        Assert.Empty(sut.SecondaryText);
        Assert.False(sut.HasSecondaryText);
    }
}
