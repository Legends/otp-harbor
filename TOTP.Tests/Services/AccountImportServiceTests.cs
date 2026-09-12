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
            [new Account(Guid.NewGuid(), "Issuer", "not*base32", "user")],
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

    [Fact]
    public async Task ImportAsync_WhenSkipExistingMatchesEveryAccount_ReturnsWithoutConfirmationOrBackup()
    {
        var existing = Enumerable.Range(1, 22)
            .Select(index => new Account(
                Guid.NewGuid(),
                "Issuer",
                "JBSWY3DPEHPK3PXP",
                $"user-{index}"))
            .ToList();
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(existing));
        var confirmationCount = 0;
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            existing,
            ImportConflictStrategy.SkipExisting,
            (_, _) =>
            {
                confirmationCount++;
                return Task.FromResult(true);
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountImportStatus.Completed, result.Value.Status);
        Assert.Equal(22, result.Value.Skipped);
        Assert.Equal(0, confirmationCount);
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Never);
        accounts.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
        accounts.Verify(value => value.UpdateAsync(It.IsAny<Account>(), It.IsAny<Account>()), Times.Never);
    }

    [Fact]
    public async Task ImportWithConflictResolutionAsync_WhenAccountWasRenamed_CanRestoreBackupVersion()
    {
        var unchangedOne = new Account(
            Guid.NewGuid(), "GitHub", "JBSWY3DPEHPK3PXP", "octocat@example.invalid");
        var backupRenamed = new Account(
            Guid.NewGuid(), "Microsoft", "JBSWY3DPEHPK3PXP", "original@example.invalid");
        var currentRenamed = new Account(
            backupRenamed.ID, "Microsoft", backupRenamed.Secret, "renamed@example.invalid");
        var unchangedTwo = new Account(
            Guid.NewGuid(), "Google", "JBSWY3DPEHPK3PXP", "user@example.invalid");
        var deletedAccount = new Account(
            Guid.NewGuid(), "Cloudflare", "JBSWY3DPEHPK3PXP", "admin@example.invalid");
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(
                [unchangedOne, currentRenamed, unchangedTwo]));
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        accounts.Setup(value => value.UpdateAsync(
                currentRenamed,
                It.IsAny<Account>()))
            .ReturnsAsync(Result.Ok());
        accounts.Setup(value => value.AddNewAsync(It.IsAny<Account>())).ReturnsAsync(Result.Ok());
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.ImportWithConflictResolutionAsync(
            [unchangedOne, backupRenamed, unchangedTwo, deletedAccount],
            (preview, _) =>
            {
                Assert.Equal(4, preview.TotalCount);
                Assert.Equal(1, preview.NewCount);
                Assert.Equal(2, preview.UnchangedCount);
                var conflict = Assert.Single(preview.ChangedConflicts);
                Assert.Equal("Microsoft: renamed@example.invalid",
                    $"{conflict.CurrentIssuer}: {conflict.CurrentAccountName}");
                Assert.Equal("Microsoft: original@example.invalid",
                    $"{conflict.BackupIssuer}: {conflict.BackupAccountName}");
                Assert.False(conflict.IssuerChanged);
                Assert.True(conflict.AccountNameChanged);
                Assert.False(conflict.SecretChanged);
                Assert.False(conflict.PeriodChanged);
                return Task.FromResult<AccountImportResolution?>(new AccountImportResolution(
                    [new AccountImportConflictResolution(
                        conflict.ImportIndex,
                        AccountImportConflictAction.Replace)]));
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountImportStatus.Completed, result.Value.Status);
        Assert.Equal(1, result.Value.Added);
        Assert.Equal(1, result.Value.Replaced);
        Assert.Equal(2, result.Value.Skipped);
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Once);
        accounts.Verify(value => value.UpdateAsync(
            currentRenamed,
            It.Is<Account>(replacement =>
                replacement.ID == currentRenamed.ID
                && replacement.AccountName == "original@example.invalid")), Times.Once);
        accounts.Verify(value => value.AddNewAsync(It.Is<Account>(added =>
            added.ID == deletedAccount.ID)), Times.Once);
    }

    [Fact]
    public async Task ImportWithConflictResolutionAsync_WhenResolutionIsIncomplete_FailsBeforeBackupOrWrites()
    {
        var existing = new Account(Guid.NewGuid(), "Issuer", "JBSWY3DPEHPK3PXP", "current");
        var incoming = new Account(existing.ID, "Issuer", existing.Secret, "backup");
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([existing]));
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.ImportWithConflictResolutionAsync(
            [incoming],
            (_, _) => Task.FromResult<AccountImportResolution?>(
                new AccountImportResolution([])),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Never);
        accounts.Verify(value => value.UpdateAsync(It.IsAny<Account>(), It.IsAny<Account>()), Times.Never);
        accounts.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
    }

    [Fact]
    public async Task ImportWithConflictResolutionAsync_DetectsEverySupportedAccountFieldChange()
    {
        var id = Guid.NewGuid();
        var existing = new Account(id, "Current issuer", "JBSWY3DPEHPK3PXP", "current", 30);
        var incoming = new Account(id, "Backup issuer", "KRSXG5DSNFXGOIDB", "backup", 60);
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([existing]));
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.ImportWithConflictResolutionAsync(
            [incoming],
            (preview, _) =>
            {
                var conflict = Assert.Single(preview.ChangedConflicts);
                Assert.True(conflict.IssuerChanged);
                Assert.True(conflict.AccountNameChanged);
                Assert.True(conflict.SecretChanged);
                Assert.True(conflict.PeriodChanged);
                return Task.FromResult<AccountImportResolution?>(new AccountImportResolution(
                    [new AccountImportConflictResolution(
                        conflict.ImportIndex,
                        AccountImportConflictAction.Skip)]));
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Skipped);
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Never);
    }
}
