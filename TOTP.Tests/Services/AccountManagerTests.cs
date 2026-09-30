using Moq;
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
    public async Task UpdateAsync_PreservesExistingGroupForNonGroupAccountEdits()
    {
        var group = new AccountGroup(Guid.NewGuid(), "Work", "#4F6BED");
        var previous = new Account(Guid.NewGuid(), "Old", "AAAA", "alice", group: group);
        var updated = new Account(previous.ID, "New", "BBBB", "alice");
        var dal = new Mock<IAccountDAL>();
        dal.Setup(value => value.UpdateAsync(It.IsAny<Account>()))
            .ReturnsAsync(FluentResults.Result.Ok());
        var sut = new AccountManager(dal.Object);

        var result = await sut.UpdateAsync(previous, updated);

        Assert.True(result.IsSuccess);
        dal.Verify(value => value.UpdateAsync(It.Is<Account>(account => account.Group == group)), Times.Once);
    }
}
