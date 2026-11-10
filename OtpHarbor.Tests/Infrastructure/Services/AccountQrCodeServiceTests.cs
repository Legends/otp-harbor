using FluentResults;
using Moq;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Infrastructure.Services;

public sealed class AccountQrCodeServiceTests
{
    [Theory]
    [MemberData(nameof(TotpConfigurationTests.Configurations), MemberType = typeof(TotpConfigurationTests))]
    public async Task GenerateAsync_ConfinesSeedAndReturnsOwnedPngBuffer(string algorithm, int digits)
    {
        var id = Guid.NewGuid();
        const string seed = "JBSWY3DPEHPK3PXP";
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(
                [new Account(id, "Issuer", seed, "account", 60, algorithm: algorithm, digits: digits)]));
        var sourcePng = new byte[] { 137, 80, 78, 71 };
        var qr = new Mock<IQrCodeService>();
        qr.Setup(value => value.BuildOtpAuthUri("Issuer", seed, "account", 60, algorithm, digits))
            .Returns("synthetic-uri");
        qr.Setup(value => value.GenerateQr("synthetic-uri")).Returns(sourcePng);
        var sut = new AccountQrCodeService(manager.Object, qr.Object);

        var result = await sut.GenerateAsync(id);

        Assert.True(result.IsSuccess);
        using var owned = result.Value;
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, owned.Memory.ToArray());
        Assert.All(sourcePng, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task GenerateAsync_WhenQrBoundaryThrows_ReturnsGenericFailure()
    {
        var id = Guid.NewGuid();
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(
                [new Account(id, "Issuer", "SENSITIVE-SEED", "account")]));
        var qr = new Mock<IQrCodeService>();
        qr.Setup(value => value.BuildOtpAuthUri(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>()))
            .Throws(new InvalidOperationException("SENSITIVE-SEED"));
        var sut = new AccountQrCodeService(manager.Object, qr.Object);

        var result = await sut.GenerateAsync(id);

        Assert.True(result.IsFailed);
        Assert.DoesNotContain("SENSITIVE", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }
}
