using TOTP.Core.Models;
using TOTP.Core.Validation;

namespace TOTP.Tests.Validation;

public sealed class AccountGroupPolicyTests
{
    [Fact]
    public void TryNormalizeStored_PreservesValidLegacyColorSoGroupRemainsVisible()
    {
        var group = new AccountGroup(Guid.NewGuid(), "Work", "#7c3aed");

        var result = AccountGroupPolicy.TryNormalizeStored(group, out var normalized);

        Assert.True(result);
        Assert.Equal("#7C3AED", normalized?.Color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("green")]
    [InlineData("#GGGGGG")]
    public void TryNormalizeStored_InvalidCosmeticColorUsesSafeDefault(string? color)
    {
        var group = new AccountGroup(Guid.NewGuid(), "Work", color!);

        var result = AccountGroupPolicy.TryNormalizeStored(group, out var normalized);

        Assert.True(result);
        Assert.Equal(AccountGroupPolicy.AllowedColors[0], normalized?.Color);
    }
}
