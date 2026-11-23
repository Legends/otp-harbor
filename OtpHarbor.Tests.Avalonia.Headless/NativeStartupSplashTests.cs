using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentResults;
using Moq;
using TOTP.Avalonia.Mobile.Localization;
using TOTP.Avalonia.Mobile.Platform;
using TOTP.Avalonia.Mobile.Presentation;
using TOTP.Avalonia.Mobile.Views;
using TOTP.Avalonia.Shared.Controls;
using TOTP.Core.Common;
using TOTP.Core.Enums;
using TOTP.Core.Models;
using TOTP.Core.Security;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Tests.Avalonia.Headless;

public sealed class NativeStartupSplashTests : IDisposable
{
    private readonly List<MainView> _views = [];
    private readonly List<MobileShellViewModel> _models = [];
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    [AvaloniaFact]
    public async Task Accounts_KeepNativeSplashUntilAllCodesAndNativeRowsAreReady()
    {
        var context = CreateContext();
        var pendingSnapshot = new TaskCompletionSource<Result<AccountListSnapshot>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.Snapshots.Setup(service => service.LoadAsync()).Returns(pendingSnapshot.Task);
        var view = CreateView(context.Model);
        view.UseNativeAccountList(new Border());
        var transitions = new List<string>();
        view.StartupContentRevealRequested += (_, _) => transitions.Add("rows-visible");
        view.NativeStartupContentReady += (_, _) => transitions.Add("native-release");

        var initializing = context.Model.InitializeAsync();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
        Assert.Empty(transitions);
        Assert.False(Overlay(view).IsVisible);
        pendingSnapshot.SetResult(Result.Ok(CreateSnapshot()));
        await initializing;
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
        Assert.Equal(40, context.Model.Accounts.Count);
        Assert.All(context.Model.Accounts, account => Assert.Equal("123456", account.Code));
        Assert.Empty(transitions);

        view.NotifyNativeAccountListRendered();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

        Assert.Equal(["rows-visible", "native-release"], transitions);
        Assert.True(context.Model.IsWaitingForStartupSurface);
        Assert.False(Overlay(view).IsVisible);
        view.NotifyNativeAccountListRendered();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
        Assert.Equal(2, transitions.Count);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnlockAndOnboarding_ReleaseNativeSplashWithoutWaitingForAccounts(bool configured)
    {
        var context = CreateContext(configured: configured, appLock: true);
        var view = CreateView(context.Model);
        var ready = 0;
        view.NativeStartupContentReady += (_, _) => ready++;

        await context.Model.InitializeAsync();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

        Assert.Equal(1, ready);
        Assert.Equal(configured, context.Model.IsUnlockVisible);
        Assert.Equal(!configured, context.Model.IsOnboardingVisible);
        Assert.False(context.Model.IsInitialAccountLoadComplete);
        Assert.True(context.Model.IsWaitingForStartupSurface);
        Assert.False(Overlay(view).IsVisible);
        context.Snapshots.Verify(service => service.LoadAsync(), Times.Never);
    }

    [AvaloniaTheory]
    [InlineData(AuthorizationResult.Cancelled)]
    [InlineData(AuthorizationResult.NotAvailable)]
    [InlineData(AuthorizationResult.Success)]
    public async Task AutomaticBiometrics_WaitForActualNativeSplashRemoval(AuthorizationResult result)
    {
        var context = CreateContext(appLock: true, method: PreferredUnlockMethod.PlatformQuickUnlock);
        context.Authorization.Setup(service => service.TryUnlockWithHelloAsync()).Returns(() =>
        {
            if (result == AuthorizationResult.Success) context.State.Unlock();
            return Task.FromResult(result);
        });
        var view = CreateView(context.Model);
        var ready = 0;
        view.NativeStartupContentReady += (_, _) => ready++;
        context.Model.OnReturnedToForeground();

        await context.Model.InitializeAsync();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

        Assert.Equal(1, ready);
        context.Authorization.Verify(service => service.TryUnlockWithHelloAsync(), Times.Never);
        view.NotifyNativeStartupSplashRemoved();
        context.Authorization.Verify(service => service.TryUnlockWithHelloAsync(), Times.Never);

        context.Model.NotifyStartupSurfaceReady();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

        context.Authorization.Verify(service => service.TryUnlockWithHelloAsync(), Times.Once);
        Assert.Equal(result == AuthorizationResult.Success, context.State.IsUnlocked);
        Assert.Equal(1, ready);
        if (result == AuthorizationResult.Success)
            Assert.Equal(40, context.Model.Accounts.Count);
        else
            Assert.True(context.Model.IsUnlockVisible);
    }

    [AvaloniaTheory]
    [InlineData("en", true)]
    [InlineData("de", true)]
    [InlineData("fr", false)]
    [InlineData("es", false)]
    public async Task FailedStartup_ReleasesNativeSplashToLocalizedRecoveryAndSupportsRetry(
        string culture, bool failSettings)
    {
        var context = CreateContext(culture: culture);
        if (failSettings)
            context.Settings.Setup(service => service.LoadAsync()).ReturnsAsync(Result.Fail<IAppSettings>("Synthetic failure"));
        else
            context.Snapshots.Setup(service => service.LoadAsync()).ReturnsAsync(Result.Fail<AccountListSnapshot>("Synthetic failure"));
        var view = CreateView(context.Model);
        var ready = 0;
        view.NativeStartupContentReady += (_, _) => ready++;

        await context.Model.InitializeAsync();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

        Assert.Equal(1, ready);
        Assert.True(context.Model.CanRetry);
        Assert.True(Overlay(view).IsVisible);
        var expected = new MobileStringCatalog(CultureInfo.GetCultureInfo(culture)).Get(MobileStringKeys.StartupFailed);
        Assert.Equal(expected, context.Model.NotificationText);
        Assert.Contains(Overlay(view).GetLogicalDescendants().OfType<TextBlock>(), text =>
            text.IsVisible && text.Text == context.Model.NotificationText);
        Assert.Contains(Overlay(view).GetLogicalDescendants().OfType<Button>(), button => button.IsVisible);

        view.NotifyNativeStartupSplashRemoved();
        context.Model.NotifyStartupSurfaceReady();
        context.Settings.Setup(service => service.LoadAsync()).ReturnsAsync(Result.Ok<IAppSettings>(context.Preferences));
        context.Snapshots.Setup(service => service.LoadAsync()).ReturnsAsync(Result.Ok(CreateSnapshot()));
        await context.Model.InitializeAsync();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

        Assert.False(context.Model.CanRetry);
        Assert.Equal(40, context.Model.Accounts.Count);
        Assert.Equal(0, Overlay(view).Opacity);
        Assert.Equal(1, ready);
    }

    [AvaloniaFact]
    public async Task EmptyVault_ReleasesNativeSplashWithoutWaitingForNonexistentNativeRows()
    {
        var context = CreateContext();
        context.Snapshots.Setup(service => service.LoadAsync()).ReturnsAsync(Result.Ok(
            new AccountListSnapshot([], new HashSet<Guid>())));
        var view = CreateView(context.Model);
        view.UseNativeAccountList(new Border());
        var ready = 0;
        view.NativeStartupContentReady += (_, _) => ready++;

        await context.Model.InitializeAsync();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

        Assert.Equal(1, ready);
        Assert.True(context.Model.HasNoAccounts);
        Assert.False(Overlay(view).IsVisible);
    }

    [AvaloniaFact]
    public async Task RecreatedStartupFailure_ReportsRecoveryAsSoonAsItsContextIsAttached()
    {
        var context = CreateContext();
        context.Settings.Setup(service => service.LoadAsync()).ReturnsAsync(Result.Fail<IAppSettings>("Synthetic failure"));
        await context.Model.InitializeAsync();
        var view = new MainView();
        view.UseNativeStartupSplash();
        _views.Add(view);
        var ready = 0;
        view.NativeStartupContentReady += (_, _) => ready++;

        view.DataContext = context.Model;
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

        Assert.Equal(1, ready);
        Assert.True(context.Model.CanRetry);
        Assert.True(Overlay(view).IsVisible);
    }

    [AvaloniaFact]
    public async Task RecreatedDuringUnlock_KeepsNativeSplashWhileAccountSnapshotIsIncomplete()
    {
        var context = CreateContext(appLock: true);
        await context.Model.InitializeAsync();
        context.Model.NotifyStartupSurfaceReady();
        context.Authorization.Setup(service => service.TryUnlockWithPasswordAsync("Synthetic password"))
            .Callback(context.State.Unlock).ReturnsAsync(AuthorizationResult.Success);
        var pending = new TaskCompletionSource<Result<AccountListSnapshot>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.Snapshots.Setup(service => service.LoadAsync()).Returns(pending.Task);
        context.Model.UnlockPassword = "Synthetic password";
        var unlocking = context.Model.UnlockAsync();
        context.Model.DeferAutomaticBiometricUnlockUntilStartupSurfaceReady();
        var view = CreateView(context.Model);
        view.UseNativeAccountList(new Border());
        var ready = 0;
        view.NativeStartupContentReady += (_, _) => ready++;

        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

        Assert.True(context.Model.IsInitialized);
        Assert.True(context.Model.IsBusy);
        Assert.False(context.Model.IsInitialAccountLoadComplete);
        Assert.Equal(0, ready);
        pending.SetResult(Result.Ok(CreateSnapshot()));
        await unlocking;
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
        Assert.Equal(0, ready);

        view.NotifyNativeAccountListRendered();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

        Assert.Equal(1, ready);
        Assert.Equal(40, context.Model.Accounts.Count);
    }

    [AvaloniaFact]
    public async Task RecreatedView_PreservesTheInitializedSessionAndReleasesItsOwnSplash()
    {
        var context = CreateContext();
        await context.Model.InitializeAsync();
        context.Model.DeferAutomaticBiometricUnlockUntilStartupSurfaceReady();
        var view = CreateView(context.Model);
        var ready = 0;
        view.NativeStartupContentReady += (_, _) => ready++;

        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

        Assert.Equal(1, ready);
        Assert.Equal(40, context.Model.Accounts.Count);
        Assert.False(Overlay(view).IsVisible);
        context.Authorization.Verify(service => service.InitializeAsync(), Times.Once);
        context.Snapshots.Verify(service => service.LoadAsync(), Times.Once);
    }

    [AvaloniaFact]
    public void DeferredIconPicker_CompiledBindingsRetainTheShellCommandAndItem()
    {
        var context = CreateContext();
        var view = CreateView(context.Model);
        var deferred = view.FindControl<DeferredContent>("DeferredAccountBrandIconPickerModalOverlay")!;
        var panel = deferred.EnsureContentCreated();
        var list = panel.GetLogicalDescendants().OfType<ListBox>()
            .Single(value => value.Name == "AccountBrandIconResultsScroller");
        var option = new MobileBrandIconOption("synthetic", "Synthetic icon");
        list.ItemsSource = new[] { option };
        deferred.IsVisible = true;
        panel.IsVisible = true;
        var window = new Window { Width = 400, Height = 800, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var button = Assert.Single(list.GetVisualDescendants().OfType<Button>(),
                value => value.Classes.Contains("brand-icon-choice"));

            Assert.Same(context.Model.SelectAccountBrandIconCommand, button.Command);
            Assert.Same(option, button.CommandParameter);
            Assert.Contains("brand-entry", button.Classes);
            Assert.Equal(option.DisplayNameForDisplay,
                Assert.Single(button.GetVisualDescendants().OfType<TextBlock>()).Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task QrFileSummary_HidesNativeAccountSurfaceAndRestoresItAfterCancel()
    {
        var context = CreateContext();
        var view = CreateView(context.Model);
        view.UseNativeAccountList(new Border());
        await context.Model.InitializeAsync();
        view.NotifyNativeAccountListRendered();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
        var nativePresenter = view.FindControl<ContentControl>("NativeAccountListPresenter")!;
        Assert.True(nativePresenter.IsVisible);

        var reviewing = context.Model.MigrationImport.WaitForFileReviewAsync(
            new AccountImportReadSummary(10), "synthetic-google-qr.png");
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

        Assert.False(nativePresenter.IsVisible);
        var summary = view.FindControl<DeferredContent>("DeferredImportFileSummary")!;
        Assert.True(summary.IsVisible);
        Assert.True(summary.IsContentCreated);
        Assert.Equal(40, context.Model.Accounts.Count);
        context.Model.MigrationImport.ClearFileCommand.Execute(null);
        Assert.False(await reviewing);
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
        Assert.True(nativePresenter.IsVisible);
        Assert.False(summary.IsVisible);
    }

    private MainView CreateView(MobileShellViewModel model)
    {
        var view = new MainView();
        view.UseNativeStartupSplash();
        _views.Add(view);
        view.DataContext = model;
        return view;
    }

    private static Grid Overlay(MainView view) => view.FindControl<Grid>("StartupLoadingOverlay")!;

    private static AccountListSnapshot CreateSnapshot() => new(
        Enumerable.Range(0, 40).Select(index => new AccountListSnapshotItem(
            Guid.NewGuid(), $"Synthetic {index:D2}", "", 30, false, [],
            new TotpGenerationResult("123456", 20, 30, "234567"))).ToArray(), new HashSet<Guid>());

    private Context CreateContext(bool configured = true, bool appLock = false,
        PreferredUnlockMethod method = PreferredUnlockMethod.Password, string culture = "en")
    {
        var state = new AuthorizationState();
        state.SetConfiguration(configured, method);
        var authorization = new Mock<IAuthorizationService>();
        authorization.SetupGet(service => service.State).Returns(state);
        authorization.Setup(service => service.InitializeAsync()).Returns(Task.CompletedTask);
        authorization.Setup(service => service.TryUnlockOnStartupAsync()).Callback(state.Unlock)
            .ReturnsAsync(AuthorizationResult.Success);
        authorization.Setup(service => service.IsHelloAvailableAsync()).ReturnsAsync(true);
        var preferences = new AppSettings { AppLockEnabled = appLock, CultureName = culture, PreferredUnlockMethod = method };
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(service => service.Current).Returns(preferences);
        settings.Setup(service => service.LoadAsync()).ReturnsAsync(Result.Ok<IAppSettings>(preferences));
        var snapshots = new Mock<IAccountListSnapshotService>();
        snapshots.Setup(service => service.LoadAsync()).ReturnsAsync(Result.Ok(CreateSnapshot()));
        var model = new MobileShellViewModel(authorization.Object,
            Mock.Of<IPasswordValidationService>(), Mock.Of<IAccountManager>(), Mock.Of<IAccountTotpService>(),
            Mock.Of<IAsyncClipboardService>(), Mock.Of<IMobileQrScanner>(), Mock.Of<IQrPayloadValidator>(),
            Mock.Of<IQrAccountImportService>(), Mock.Of<IAccountQrCodeService>(), Mock.Of<IMobileQrImageFactory>(),
            Mock.Of<IMobileDocumentService>(), Mock.Of<IExportService>(), Mock.Of<IAccountImportService>(),
            settings.Object, Mock.Of<IPlatformApplicationPaths>(), new MobileStringCatalog(CultureInfo.GetCultureInfo(culture)),
            TimeProvider.System, accountListSnapshots: snapshots.Object);
        model.DeferAutomaticBiometricUnlockUntilStartupSurfaceReady();
        _models.Add(model);
        return new Context(model, state, authorization, settings, preferences, snapshots);
    }

    public void Dispose()
    {
        foreach (var model in _models) model.Dispose();
        foreach (var view in _views)
        {
            view.IsVisible = false;
            view.DataContext = null;
            view.Content = null;
        }
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }

    private sealed record Context(MobileShellViewModel Model, AuthorizationState State,
        Mock<IAuthorizationService> Authorization, Mock<ISettingsService> Settings,
        AppSettings Preferences, Mock<IAccountListSnapshotService> Snapshots);
}
