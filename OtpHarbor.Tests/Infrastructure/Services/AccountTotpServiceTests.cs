using FluentResults;
using Moq;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Infrastructure.Services;

public sealed class AccountTotpServiceTests
{
    [Fact]
    public async Task GenerateAsync_UsesMatchingAccountSeedBehindServiceBoundary()
    {
        var id = Guid.NewGuid();
        const string syntheticSeed = "JBSWY3DPEHPK3PXP";
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(
                [new Account(id, "Issuer", syntheticSeed, "account", 60)]));
        var generator = new Mock<ITotpGenerator>();
        generator.Setup(value => value.Generate(syntheticSeed, 60))
            .Returns(new TotpGenerationResult("123456", 45, 60));
        var sut = new AccountTotpService(manager.Object, generator.Object);

        var result = await sut.GenerateAsync(id);

        Assert.True(result.IsSuccess);
        Assert.Equal("123456", result.Value.Code);
        Assert.Equal(60, result.Value.PeriodSeconds);
        generator.Verify(value => value.Generate(syntheticSeed, 60), Times.Once);
    }

    [Fact]
    public async Task GenerateAsync_WhenSeedIsInvalid_ReturnsGenericFailure()
    {
        var id = Guid.NewGuid();
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(
                [new Account(id, "Issuer", "INVALID-SECRET", "account")]));
        var generator = new Mock<ITotpGenerator>();
        generator.Setup(value => value.Generate(It.IsAny<string>(), It.IsAny<int>()))
            .Throws(new FormatException("INVALID-SECRET"));
        var sut = new AccountTotpService(manager.Object, generator.Object);

        var result = await sut.GenerateAsync(id);

        Assert.True(result.IsFailed);
        Assert.DoesNotContain("INVALID-SECRET", result.Errors[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateManyAsync_LoadsAccountsOnceAndIsolatesInvalidSeed()
    {
        var validId = Guid.NewGuid();
        var invalidId = Guid.NewGuid();
        const string validSeed = "JBSWY3DPEHPK3PXP";
        const string invalidSeed = "INVALID-SECRET";
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(
            [
                new(validId, "Valid", validSeed, "account"),
                new(invalidId, "Invalid", invalidSeed, "account")
            ]));
        var generator = new Mock<ITotpGenerator>();
        generator.Setup(value => value.Generate(validSeed, 30))
            .Returns(new TotpGenerationResult("123456", 15, 30));
        generator.Setup(value => value.Generate(invalidSeed, 30))
            .Throws(new FormatException(invalidSeed));
        var sut = new AccountTotpService(manager.Object, generator.Object);

        var result = await sut.GenerateManyAsync([validId, invalidId]);

        Assert.True(result.IsSuccess);
        Assert.Equal("123456", result.Value.Codes[validId].Code);
        Assert.Contains(invalidId, result.Value.FailedAccountIds);
        Assert.DoesNotContain(validId, result.Value.FailedAccountIds);
        manager.Verify(value => value.GetAllOtpEntriesSortedAsync(), Times.Once);
    }
}
