using Moq;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Services;

public sealed class AccountManagerTests
{
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
