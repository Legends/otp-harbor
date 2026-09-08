using System.Text.Json;
using TOTP.Core.Models;
using TOTP.Core.Validation;

namespace TOTP.Tests.Models;

public sealed class AccountPeriodTests
{
    [Fact]
    public void Deserialize_WhenLegacyJsonHasNoPeriod_UsesThirtySecondDefault()
    {
        var id = Guid.NewGuid();
        var json = $$"""
            {
              "id": "{{id}}",
              "issuer": "Example",
              "secret": "JBSWY3DPEHPK3PXP",
              "account_name": "alice"
            }
            """;

        var account = JsonSerializer.Deserialize<Account>(json);

        Assert.NotNull(account);
        Assert.Equal(TotpPeriodPolicy.DefaultSeconds, account.PeriodSeconds);
    }

    [Fact]
    public void SerializeAndDeserialize_WithCustomPeriod_PreservesPeriod()
    {
        var source = new Account(
            Guid.NewGuid(),
            "Example",
            "JBSWY3DPEHPK3PXP",
            "alice",
            60);

        var json = JsonSerializer.Serialize(source);
        var account = JsonSerializer.Deserialize<Account>(json);

        Assert.NotNull(account);
        Assert.Equal(60, account.PeriodSeconds);
        Assert.Contains("\"period\":60", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(30, true)]
    [InlineData(600, true)]
    [InlineData(3600, true)]
    [InlineData(3601, false)]
    public void IsSupported_EnforcesBoundedPeriod(int periodSeconds, bool expected)
    {
        Assert.Equal(expected, TotpPeriodPolicy.IsSupported(periodSeconds));
    }
}
