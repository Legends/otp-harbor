using FluentResults;
using Moq;
using TOTP.Core.Models;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Models;

namespace TOTP.Tests.Avalonia.Mobile;

public sealed partial class MobileShellViewModelTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ImportSummaryAndFormatChoice_HideNativeSurfacesUntilCompleted(bool formatChoice, bool confirm)
    {
        var account = new Account(Guid.NewGuid(), "Synthetic", ValidSecret, isFavorite: true);
        var context = CreateContext(isConfigured: true, accounts: [account]);
        using var sut = context.Sut;
        context.Authorization.Setup(service => service.TryUnlockWithPasswordAsync("synthetic password"))
            .Callback(context.State.Unlock).ReturnsAsync(AuthorizationResult.Success);
        await sut.InitializeAsync();
        sut.UnlockPassword = "synthetic password";
        await sut.UnlockAsync();
        Assert.True(sut.IsNativeAccountListVisible);
        var original = Assert.Single(sut.Accounts);
        var notifications = new List<string?>();
        sut.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);

        var formats = Enum.GetValues<AccountImportFormat>().Take(2).ToArray();
        var formatTask = formatChoice
            ? sut.MigrationImport.ChooseDetectedFormatAsync(formats, "synthetic.json", Xunit.TestContext.Current.CancellationToken)
            : null;
        var summaryTask = formatChoice ? null : sut.MigrationImport.WaitForFileReviewAsync(
            new AccountImportReadSummary(10), "synthetic-google-qr.png", Xunit.TestContext.Current.CancellationToken);

        Assert.False(sut.IsNativeAccountListVisible);
        Assert.False(sut.IsNativeAccountGroupsVisible);
        Assert.True(sut.CanHandleSystemBack);
        Assert.Contains(nameof(sut.IsNativeAccountListVisible), notifications);
        Assert.Contains(nameof(sut.IsNativeAccountGroupsVisible), notifications);
        notifications.Clear();
        if (!confirm) sut.MigrationImport.ClearFileCommand.Execute(null);
        else if (formatChoice)
        {
            sut.MigrationImport.SelectedFormat = sut.MigrationImport.FormatOptions[0];
            sut.MigrationImport.UseFormatCommand.Execute(null);
        }
        else sut.MigrationImport.ReviewFileCommand.Execute(null);

        if (formatTask is not null) Assert.Equal(confirm ? formats[0] : (AccountImportFormat?)null, await formatTask);
        if (summaryTask is not null) Assert.Equal(confirm, await summaryTask);
        Assert.True(sut.IsNativeAccountListVisible);
        Assert.Equal(sut.HasAccountNavigationCards, sut.IsNativeAccountGroupsVisible);
        Assert.Same(original, Assert.Single(sut.Accounts));
        Assert.Contains(nameof(sut.IsNativeAccountListVisible), notifications);
    }
}
