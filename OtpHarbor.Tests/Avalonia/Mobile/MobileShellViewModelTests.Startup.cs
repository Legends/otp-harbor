using FluentResults;
using Moq;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Tests.Avalonia.Mobile;

public sealed partial class MobileShellViewModelTests
{
    [Fact]
    public async Task UnlockedStartup_WaitsForEveryAccountCodeAndIconMatchButNotTheFullPickerCatalog()
    {
        var icons = new Mock<IBrandIconPackService>();
        var accountIcons = icons.As<IAccountBrandIconInitializer>();
        var matchesReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalogReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IReadOnlyCollection<string>? requestedIssuers = null;
        accountIcons.Setup(service => service.InitializeForAccountsAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<string>, CancellationToken>((issuers, _) => requestedIssuers = issuers)
            .Returns(matchesReady.Task);
        icons.Setup(service => service.InitializeAsync(It.IsAny<CancellationToken>())).Returns(catalogReady.Task);
        var snapshots = new Mock<IAccountListSnapshotService>();
        var items = Enumerable.Range(0, 40).Select(index => new AccountListSnapshotItem(
            Guid.NewGuid(), $"Issuer {index:D2}", "Synthetic account", 30, false, [],
            new TotpGenerationResult("123456", 20, 30, "234567"))).ToArray();
        snapshots.Setup(service => service.LoadAsync()).ReturnsAsync(Result.Ok(
            new AccountListSnapshot(items, new HashSet<Guid>())));
        var context = CreateContext(isConfigured: true, appLockEnabled: false,
            brandIconPackService: icons.Object, accountListSnapshots: snapshots.Object);
        using var sut = context.Sut;

        var initializing = sut.InitializeAsync();

        Assert.False(initializing.IsCompleted);
        Assert.False(sut.IsInitialAccountLoadComplete);
        Assert.Empty(sut.Accounts);
        Assert.Equal(items.Select(item => item.Issuer), requestedIssuers);
        icons.Verify(service => service.InitializeAsync(It.IsAny<CancellationToken>()), Times.Never);

        matchesReady.SetResult();
        await initializing.WaitAsync(TimeSpan.FromSeconds(5), global::Xunit.TestContext.Current.CancellationToken);

        Assert.True(sut.IsInitialAccountLoadComplete);
        Assert.Equal(40, sut.Accounts.Count);
        Assert.All(sut.Accounts, account => Assert.Equal("123456", account.Code));
        Assert.False(sut.IsBusy);
        icons.Verify(service => service.InitializeAsync(It.IsAny<CancellationToken>()), Times.Never);
        sut.ShowSettingsCommand.Execute(null);
        Assert.True(sut.IsSettingsCategoryListVisible);

        sut.PreloadSecondaryResources();
        icons.Verify(service => service.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(40, sut.Accounts.Count);
        catalogReady.SetResult();
    }
}
