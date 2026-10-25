using FluentResults;
using Moq;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Infrastructure.Services;

public sealed class AccountListSnapshotServiceTests
{
    private const string ValidSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"; // gitleaks:allow - synthetic Base32 fixture

    [Fact]
    public async Task LoadAsync_DecryptsVaultOnceAndReturnsSortedSecretFreeSnapshotsWithCodes()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var dal = new Mock<IAccountDAL>();
        dal.Setup(value => value.GetAllAsync()).ReturnsAsync(Result.Ok(new List<Account>
        {
            new(secondId, "Zulu", "SECONDSECRET", "second", 60),
            new(firstId, "Alpha", "FIRSTSECRET", "first", 30, isFavorite: true)
        }));
        var generator = new Mock<ITotpGenerator>();
        generator.Setup(value => value.Generate("FIRSTSECRET", 30))
            .Returns(new TotpGenerationResult("111111", 12, 30, "222222"));
        generator.Setup(value => value.Generate("SECONDSECRET", 60))
            .Returns(new TotpGenerationResult("333333", 42, 60, "444444"));
        var sut = new AccountListSnapshotService(dal.Object, generator.Object);

        var result = await sut.LoadAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(["Alpha", "Zulu"], result.Value.Accounts.Select(value => value.Issuer));
        Assert.Equal("111111", result.Value.Accounts[0].Totp?.Code);
        Assert.True(result.Value.Accounts[0].IsFavorite);
        Assert.Empty(result.Value.FailedAccountIds);
        Assert.DoesNotContain(
            typeof(AccountListSnapshotItem).GetProperties(),
            property => property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        dal.Verify(value => value.GetAllAsync(), Times.Once);
    }

    [Fact]
    public async Task LoadAsync_InvalidSeedKeepsAccountAndReportsOnlyItsIdentifier()
    {
        var accountId = Guid.NewGuid();
        var dal = new Mock<IAccountDAL>();
        dal.Setup(value => value.GetAllAsync()).ReturnsAsync(Result.Ok(new List<Account>
        {
            new(accountId, "Issuer", "INVALID-SECRET", "account")
        }));
        var generator = new Mock<ITotpGenerator>();
        generator.Setup(value => value.Generate("INVALID-SECRET", 30))
            .Throws(new FormatException("INVALID-SECRET"));
        var sut = new AccountListSnapshotService(dal.Object, generator.Object);

        var result = await sut.LoadAsync();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Accounts);
        Assert.Null(result.Value.Accounts[0].Totp);
        Assert.Contains(accountId, result.Value.FailedAccountIds);
        dal.Verify(value => value.GetAllAsync(), Times.Once);
    }

    [Fact]
    public async Task LoadAsync_WithSixHundredAccounts_UsesOneVaultReadAndOneGenerationPerAccount()
    {
        var accounts = Enumerable.Range(0, 600)
            .Select(index => new Account(
                Guid.NewGuid(),
                $"Issuer {index:D3}",
                ValidSecret,
                $"account-{index:D3}"))
            .ToList();
        var dal = new Mock<IAccountDAL>();
        dal.Setup(value => value.GetAllAsync()).ReturnsAsync(Result.Ok(accounts));
        var generator = new Mock<ITotpGenerator>();
        generator.Setup(value => value.Generate(ValidSecret, 30))
            .Returns(new TotpGenerationResult("123456", 20, 30, "234567"));
        var sut = new AccountListSnapshotService(dal.Object, generator.Object);

        var result = await sut.LoadAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(600, result.Value.Accounts.Count);
        Assert.Empty(result.Value.FailedAccountIds);
        dal.Verify(value => value.GetAllAsync(), Times.Once);
        generator.Verify(value => value.Generate(ValidSecret, 30), Times.Exactly(600));
    }
}
