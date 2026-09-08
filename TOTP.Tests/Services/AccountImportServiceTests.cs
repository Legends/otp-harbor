using FluentResults;
using Moq;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Services;

public sealed class AccountImportServiceTests
{
    [Fact]
    public async Task ImportAsync_WhenAnyTargetIsInvalid_DoesNotReadOrMutateVault()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var accounts = new Mock<IAccountManager>(MockBehavior.Strict);
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            [new Account(Guid.NewGuid(), "Issuer", "not-base32", "user")],
            ImportConflictStrategy.SkipExisting,
            (_, _) => Task.FromResult(true),
            cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountImportStatus.InvalidTargets, result.Value.Status);
        accounts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ImportAsync_WhenPeriodIsOutsidePolicy_DoesNotReadOrMutateVault()
    {
        var accounts = new Mock<IAccountManager>(MockBehavior.Strict);
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            [new Account(Guid.NewGuid(), "Issuer", "JBSWY3DPEHPK3PXP", "user", 3601)],
            ImportConflictStrategy.SkipExisting,
            (_, _) => Task.FromResult(true),
            TestContext.Current.CancellationToken);

        Assert.Equal(AccountImportStatus.InvalidTargets, result.Value.Status);
        accounts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ImportAsync_WithKeepBoth_AssignsNewIdentityAndCollisionFreeIssuer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var existing = new Account(Guid.NewGuid(), "Issuer", "JBSWY3DPEHPK3PXP", "user");
        var incoming = new Account(existing.ID, "Issuer", "KRSXG5DSNFXGOIDB", "user");
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([existing]));
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        accounts.Setup(value => value.AddNewAsync(It.IsAny<Account>())).ReturnsAsync(Result.Ok());
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            [incoming],
            ImportConflictStrategy.KeepBoth,
            (preview, _) => Task.FromResult(preview.ConflictCount == 1),
            cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountImportStatus.Completed, result.Value.Status);
        Assert.Equal(1, result.Value.Added);
        accounts.Verify(value => value.AddNewAsync(It.Is<Account>(account =>
            account.ID != existing.ID
            && account.Issuer == "Issuer (imported)"
            && account.PeriodSeconds == 30)), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_WhenConfirmationDeclined_DoesNotCreateBackupOrWrite()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]));
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            [new Account(Guid.NewGuid(), "Issuer", "JBSWY3DPEHPK3PXP", "user")],
            ImportConflictStrategy.SkipExisting,
            (_, _) => Task.FromResult(false),
            cancellationToken);

        Assert.Equal(AccountImportStatus.Cancelled, result.Value.Status);
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Never);
        accounts.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
    }
}
