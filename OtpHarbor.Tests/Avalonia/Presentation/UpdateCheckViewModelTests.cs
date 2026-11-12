using FluentResults;
using Avalonia.Controls;
using Moq;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Tests.Avalonia.Presentation;

public sealed class UpdateCheckViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DistributionPolicy_OnlyStoreHidesUpdatesAndSuppressesChecking(bool store)
    {
        var updates = new Mock<IPortableUpdateService>();
        updates.Setup(value => value.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new PortableUpdateCheckResult(PortableUpdateCheckStatus.NoUpdate)));
        using var sut = new UpdateCheckViewModel(updates.Object,
            Mock.Of<IUpdateInstallerLauncher>(), Localization(), isStoreDistribution: store);

        Assert.Equal(!store, sut.IsSoftwareUpdatesVisible);
        Assert.Equal(!store, sut.ShowCheckAction);
        Assert.Equal(!store, sut.CheckCommand.CanExecute(null));
        Assert.Equal(!store, sut.Notification.HasMessage);
        await sut.CheckAsync();

        updates.Verify(value => value.CheckAsync(It.IsAny<CancellationToken>()), store ? Times.Never() : Times.Once());
        if (store)
        {
            Assert.Empty(sut.Message);
            Assert.False(sut.ShowDownloadAction);
            Assert.False(sut.ShowInstallAction);
            Assert.False(sut.ShowCancelAction);
        }
    }

    [Fact]
    public async Task Constructor_WhenGermanIsActive_UsesLocalizedTransientReadyMessage()
    {
        var localization = new AvaloniaLocalizationService(
            new ResourceDictionary(),
            new AvaloniaStringCatalog());
        localization.ApplyCulture("de");
        using var sut = new UpdateCheckViewModel(
            Mock.Of<IPortableUpdateService>(),
            Mock.Of<IUpdateInstallerLauncher>(),
            localization,
            TimeSpan.FromMilliseconds(20));

        Assert.Equal(
            "Prüfen Sie den konfigurierten signierten Update-Feed, wenn Sie bereit sind.",
            sut.Message);
        await WaitUntilMessageClearsAsync(sut);
        Assert.Empty(sut.Message);
    }

    [Fact]
    public async Task CheckAsync_WhenSignedOfferAvailable_ShowsVersionAndReleaseNotesWithoutDownloading()
    {
        var offer = CreateOffer();
        var updates = new Mock<IPortableUpdateService>();
        updates.Setup(value => value.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new PortableUpdateCheckResult(
                PortableUpdateCheckStatus.UpdateAvailable,
                offer)));
        using var sut = new UpdateCheckViewModel(
            updates.Object,
            Mock.Of<IUpdateInstallerLauncher>(),
            Localization(),
            TimeSpan.FromMilliseconds(20));

        Assert.True(sut.ShowCheckAction);
        Assert.False(sut.ShowDownloadAction);

        await sut.CheckAsync();

        Assert.Equal("9.0.0", sut.Version);
        Assert.False(sut.ShowCheckAction);
        Assert.True(sut.ShowDownloadAction);
        Assert.Equal("Security and reliability improvements.", sut.ReleaseNotes);
        Assert.Contains("Download starts only", sut.Message, StringComparison.OrdinalIgnoreCase);
        updates.Verify(value => value.DownloadAsync(
            It.IsAny<PortableUpdateOffer>(),
            It.IsAny<IProgress<PortableUpdateDownloadProgress>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        await WaitUntilMessageClearsAsync(sut);
        Assert.Empty(sut.Message);
    }

    [Fact]
    public async Task DownloadAsync_WhenVerifiedPackageReady_ExposesInstallReadyState()
    {
        var offer = CreateOffer();
        var package = new PortableUpdatePackage(
            offer.Version, "synthetic.ready", offer.ArtifactSignature, "public-key");
        var updates = new Mock<IPortableUpdateService>();
        updates.Setup(value => value.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new PortableUpdateCheckResult(
                PortableUpdateCheckStatus.UpdateAvailable,
                offer)));
        updates.Setup(value => value.DownloadAsync(
                offer,
                It.IsAny<IProgress<PortableUpdateDownloadProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(package));
        var installer = new Mock<IUpdateInstallerLauncher>();
        installer.SetupGet(value => value.IsSupported).Returns(true);
        using var sut = new UpdateCheckViewModel(
            updates.Object,
            installer.Object,
            Localization(),
            TimeSpan.FromMilliseconds(20));
        await sut.CheckAsync();

        await sut.DownloadAsync();

        Assert.True(sut.IsInstallReady);
        Assert.False(sut.ShowDownloadAction);
        Assert.True(sut.ShowInstallAction);
        Assert.Equal(100, sut.ProgressPercentage);
        Assert.Equal(NotificationSeverity.Success, sut.MessageSeverity);
        await WaitUntilMessageClearsAsync(sut);
        Assert.Empty(sut.Message);
    }

    [Fact]
    public async Task InstallAsync_WhenPlatformAdapterFails_ReportsRecoverableFailure()
    {
        var offer = CreateOffer();
        var package = new PortableUpdatePackage(
            offer.Version, "synthetic.ready", offer.ArtifactSignature, "public-key");
        var updates = new Mock<IPortableUpdateService>();
        updates.Setup(value => value.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new PortableUpdateCheckResult(
                PortableUpdateCheckStatus.UpdateAvailable,
                offer)));
        updates.Setup(value => value.DownloadAsync(
                offer,
                It.IsAny<IProgress<PortableUpdateDownloadProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(package));
        var installer = new Mock<IUpdateInstallerLauncher>();
        installer.SetupGet(value => value.IsSupported).Returns(true);
        installer.Setup(value => value.LaunchAsync(package, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail("sensitive installer detail"));
        using var sut = new UpdateCheckViewModel(
            updates.Object,
            installer.Object,
            Localization(),
            TimeSpan.FromMilliseconds(20));
        await sut.CheckAsync();
        await sut.DownloadAsync();
        string? failureMessage = null;
        sut.Notification.Shown += (_, shown) => failureMessage = shown.Text;

        await sut.InstallAsync();

        Assert.Equal(NotificationSeverity.Error, sut.MessageSeverity);
        Assert.Equal(AvaloniaStringKeys.UpdateInstallerStartFailed, failureMessage);
        Assert.True(sut.IsInstallReady);
        Assert.True(sut.ShowInstallAction);
        Assert.False(sut.IsBusy);
        Assert.DoesNotContain("sensitive", failureMessage, StringComparison.OrdinalIgnoreCase);
        await WaitUntilMessageClearsAsync(sut);
        Assert.Empty(sut.Message);
    }

    [Fact]
    public async Task CheckAsync_WhenVerificationFails_DoesNotExposeErrorOrOffer()
    {
        var updates = new Mock<IPortableUpdateService>();
        updates.Setup(value => value.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<PortableUpdateCheckResult>(new PortableUpdateError(
                PortableUpdateErrorCode.FeedVerificationFailed,
                "signature detail")));
        using var sut = new UpdateCheckViewModel(
            updates.Object,
            Mock.Of<IUpdateInstallerLauncher>(),
            Localization());

        await sut.CheckAsync();

        Assert.False(sut.HasOffer);
        Assert.Equal(NotificationSeverity.Error, sut.MessageSeverity);
        Assert.DoesNotContain("signature detail", sut.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckAsync_WhenFeedIsUnavailable_DoesNotClaimSignatureVerificationFailed()
    {
        var updates = new Mock<IPortableUpdateService>();
        updates.Setup(value => value.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<PortableUpdateCheckResult>(new PortableUpdateError(
                PortableUpdateErrorCode.FeedUnavailable,
                "transport detail")));
        using var sut = new UpdateCheckViewModel(
            updates.Object,
            Mock.Of<IUpdateInstallerLauncher>(),
            Localization());

        await sut.CheckAsync();

        Assert.False(sut.HasOffer);
        Assert.Equal(NotificationSeverity.Error, sut.MessageSeverity);
        Assert.Equal(AvaloniaStringKeys.UpdateCheckFailed, sut.Message);
        Assert.DoesNotContain("transport detail", sut.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WaitUntilMessageClearsAsync(UpdateCheckViewModel viewModel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (viewModel.Message.Length > 0)
            await Task.Delay(10, timeout.Token);
    }

    private static PortableUpdateOffer CreateOffer() => new(
        new Version(9, 0, 0),
        new Uri("https://example.invalid/update.zip"),
        Convert.ToBase64String(new byte[64]),
        "Security and reliability improvements.");

    private static IAvaloniaLocalizationService Localization()
    {
        var localization = new Mock<IAvaloniaLocalizationService>();
        localization.Setup(value => value.GetString(It.IsAny<string>()))
            .Returns((string key) => key == AvaloniaStringKeys.UpdateAvailable
                ? "Signed update {0} is available. Download starts only when you choose Download."
                : key);
        return localization.Object;
    }
}
