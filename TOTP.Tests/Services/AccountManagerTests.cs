using Moq;
using TOTP.Core.Backup;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Services;

public sealed class AccountManagerTests
{
    [Fact]
    public async Task CommitImportAsync_DelegatesCompleteSnapshotOnce()
    {
        var snapshot = new[]
        {
            new Account(Guid.NewGuid(), "One", "JBSWY3DPEHPK3PXP"),
            new Account(Guid.NewGuid(), "Two", "KRSXG5DSNFXGOIDB")
        };
        var dal = new Mock<IAccountDAL>();
        dal.Setup(value => value.CommitImportAsync(snapshot)).ReturnsAsync(FluentResults.Result.Ok());
        var sut = new AccountManager(dal.Object);

        var result = await sut.CommitImportAsync(snapshot);

        Assert.True(result.IsSuccess);
        dal.Verify(value => value.CommitImportAsync(snapshot), Times.Once);
    }

    [Fact]
    public async Task SaveFavoritesAsync_WithSelections_DelegatesAtomicUpdate()
    {
        var selected = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var dal = new Mock<IAccountDAL>();
        dal.Setup(value => value.SaveFavoritesAsync(selected))
            .ReturnsAsync(FluentResults.Result.Ok());
        var sut = new AccountManager(dal.Object);

        var result = await sut.SaveFavoritesAsync(selected);

        Assert.True(result.IsSuccess);
        dal.Verify(value => value.SaveFavoritesAsync(selected), Times.Once);
    }

    [Fact]
    public async Task SaveFavoritesAsync_WithoutSelections_RejectsWithoutWriting()
    {
        var dal = new Mock<IAccountDAL>();
        var sut = new AccountManager(dal.Object);

        var result = await sut.SaveFavoritesAsync([]);

        Assert.True(result.IsFailed);
        dal.Verify(value => value.SaveFavoritesAsync(It.IsAny<IReadOnlyCollection<Guid>>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_PreservesAllExistingGroupsForNonGroupAccountEdits()
    {
        var groups = new[]
        {
            new AccountGroup(Guid.NewGuid(), "Work", "#4F6BED"),
            new AccountGroup(Guid.NewGuid(), "Personal", "#18A999")
        };
        var previous = new Account(
            Guid.NewGuid(),
            "Old",
            "AAAA",
            "alice",
            groups: groups);
        var updated = new Account(previous.ID, "New", "BBBB", "alice");
        var dal = new Mock<IAccountDAL>();
        dal.Setup(value => value.UpdateAsync(It.IsAny<Account>()))
            .ReturnsAsync(FluentResults.Result.Ok());
        var sut = new AccountManager(dal.Object);

        var result = await sut.UpdateAsync(previous, updated);

        Assert.True(result.IsSuccess);
        dal.Verify(value => value.UpdateAsync(It.Is<Account>(account =>
            account.Groups.SequenceEqual(groups))), Times.Once);
    }

    [Fact]
    public async Task AddNewAsync_AfterSuccessfulMutation_RequestsAutomaticBackup()
    {
        var account = new Account(Guid.NewGuid(), "Issuer", "JBSWY3DPEHPK3PXP");
        var dal = new Mock<IAccountDAL>();
        dal.Setup(value => value.AddNewAsync(account)).ReturnsAsync(FluentResults.Result.Ok());
        var backup = new Mock<IAutomaticBackupCoordinator>();
        var sut = new AccountManager(dal.Object, backup.Object);

        var result = await sut.AddNewAsync(account);

        Assert.True(result.IsSuccess);
        backup.Verify(value => value.RequestBackup(), Times.Once);
    }

    [Fact]
    public async Task AddNewAsync_AfterFailedMutation_DoesNotRequestAutomaticBackup()
    {
        var account = new Account(Guid.NewGuid(), "Issuer", "JBSWY3DPEHPK3PXP");
        var dal = new Mock<IAccountDAL>();
        dal.Setup(value => value.AddNewAsync(account)).ReturnsAsync(FluentResults.Result.Fail("failed"));
        var backup = new Mock<IAutomaticBackupCoordinator>();
        var sut = new AccountManager(dal.Object, backup.Object);

        var result = await sut.AddNewAsync(account);

        Assert.True(result.IsFailed);
        backup.Verify(value => value.RequestBackup(), Times.Never);
    }
}
