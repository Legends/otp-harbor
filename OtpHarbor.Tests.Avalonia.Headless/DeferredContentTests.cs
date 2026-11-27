using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FluentResults;
using Moq;
using System.Globalization;
using TOTP.Avalonia.Mobile.Localization;
using TOTP.Avalonia.Mobile.Platform;
using TOTP.Avalonia.Mobile.Presentation;
using TOTP.Avalonia.Mobile.Views;
using TOTP.Avalonia.Shared.Controls;
using TOTP.Core.Enums;
using TOTP.Core.Common;
using TOTP.Core.Models;
using TOTP.Core.Security;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Tests.Avalonia.Headless;

public sealed class DeferredContentTests : IDisposable
{
    private readonly List<MainView> _views = [];

    private MainView CreateView()
    {
        var view = new MainView();
        _views.Add(view);
        return view;
    }

    public void Dispose()
    {
        foreach (var view in _views)
        {
            view.IsVisible = false;
            view.DataContext = null;
            view.Content = null;
        }
    }

    [AvaloniaFact]
    public void DeferredView_IsCreatedOnDemandAndRetainedAcrossNavigation()
    {
        var created = 0;
        var host = new DeferredContent
        {
            Template = new FuncDataTemplate<object>((_, _) =>
            {
                created++;
                return new TextBox { Text = "Synthetic input" };
            })
        };
        Assert.Equal(0, created);

        host.IsRequested = true;
        var content = Assert.IsType<TextBox>(host.Child);
        content.Text = "Edited input";
        host.IsRequested = false;
        host.Preload(360);
        host.IsRequested = true;

        Assert.Equal(1, created);
        Assert.Same(content, host.Child);
        Assert.Equal("Edited input", content.Text);
    }

    [AvaloniaFact]
    public void PreloadingHiddenContent_PreservesVisibilityAndDoesNotRevealIt()
    {
        var visibility = new Border { IsVisible = false };
        var content = new TextBox { Text = "Synthetic input" };
        content.Bind(Visual.IsVisibleProperty, new Binding(nameof(Visual.IsVisible)) { Source = visibility });
        var host = new DeferredContent
        {
            IsVisible = false,
            Template = new FuncDataTemplate<object>((_, _) => content)
        };

        host.Preload(360);

        Assert.True(host.IsContentCreated);
        Assert.False(host.IsVisible);
        Assert.False(content.IsVisible);
        Assert.False(host.IsRequested);
        visibility.IsVisible = true;
        Assert.True(content.IsVisible);
    }

    [AvaloniaTheory]
    [InlineData(360)]
    [InlineData(430)]
    [InlineData(768)]
    public void MobileSecondaryViews_AreInitiallyDeferredAndCanBePreparedAtEachViewport(int width)
    {
        var view = CreateView();
        var window = new Window { Content = view, Width = width, Height = 640 };
        try
        {
            window.Show();
            view.Measure(new Size(width, 640));
            view.Arrange(new Rect(0, 0, width, 640));
            var hosts = view.GetLogicalDescendants().OfType<DeferredContent>().ToArray();
            Assert.NotEmpty(hosts);
            Assert.All(hosts, host => Assert.False(host.IsContentCreated));
            Assert.NotNull(view.FindControl<StackPanel>("SettingsRoot"));

            foreach (var host in hosts)
            {
                host.IsVisible = false;
                host.Preload(width);
                Assert.NotNull(host.Child);
            }

            Assert.All(hosts, host => Assert.True(host.IsContentCreated));
            var appearance = view.FindControl<DeferredContent>("DeferredAppearanceSettings")!;
            appearance.IsRequested = true;
            var created = appearance.Child;
            appearance.IsRequested = false;
            appearance.IsRequested = true;
            Assert.Same(created, appearance.Child);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(360)]
    [InlineData(430)]
    public async Task SettingsNavigationBeforePreloading_ShowsLandingAndSelectedCategoryImmediately(int width)
    {
        var state = new AuthorizationState();
        state.SetConfiguration(true, PreferredUnlockMethod.Password);
        var authorization = new Mock<IAuthorizationService>();
        authorization.SetupGet(value => value.State).Returns(state);
        authorization.Setup(value => value.TryUnlockOnStartupAsync())
            .Callback(state.Unlock).ReturnsAsync(AuthorizationResult.Success);
        var settings = new Mock<ISettingsService>();
        var preferences = new AppSettings { AppLockEnabled = false, CultureName = "en" };
        settings.SetupGet(value => value.Current).Returns(preferences);
        settings.Setup(value => value.LoadAsync()).ReturnsAsync(Result.Ok<IAppSettings>(preferences));
        var snapshots = new Mock<IAccountListSnapshotService>();
        snapshots.Setup(value => value.LoadAsync()).ReturnsAsync(Result.Ok(new AccountListSnapshot(
            Enumerable.Range(0, 40).Select(index => new AccountListSnapshotItem(
                Guid.NewGuid(), $"Synthetic {index:D2}", "", 30, false, [],
                new TotpGenerationResult("123456", 20, 30, "234567"))).ToArray(), new HashSet<Guid>())));
        using var model = new MobileShellViewModel(authorization.Object,
            Mock.Of<IPasswordValidationService>(), Mock.Of<IAccountManager>(), Mock.Of<IAccountTotpService>(),
            Mock.Of<IAsyncClipboardService>(), Mock.Of<IMobileQrScanner>(), Mock.Of<IQrPayloadValidator>(),
            Mock.Of<IQrAccountImportService>(), Mock.Of<IAccountQrCodeService>(), Mock.Of<IMobileQrImageFactory>(),
            Mock.Of<IMobileDocumentService>(), Mock.Of<IExportService>(), Mock.Of<IAccountImportService>(),
            settings.Object, Mock.Of<IPlatformApplicationPaths>(), new MobileStringCatalog(CultureInfo.GetCultureInfo("en")),
            TimeProvider.System, accountListSnapshots: snapshots.Object);
        var view = CreateView();
        view.DataContext = model;

        await model.InitializeAsync();
        Assert.Equal(40, model.Accounts.Count);
        var security = view.FindControl<DeferredContent>("DeferredAppLockPanel")!;
        Assert.False(security.IsContentCreated);

        var availability = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        authorization.Setup(value => value.IsHelloAvailableAsync()).Returns(availability.Task);
        var opening = model.ShowSettingsAsync();

        Assert.False(opening.IsCompleted);
        Assert.True(view.FindControl<StackPanel>("SettingsRoot")!.IsVisible);
        Assert.True(model.IsSettingsCategoryListVisible);
        Assert.False(security.IsContentCreated);
        model.ShowSecuritySettingsCommand.Execute(null);
        Assert.True(model.IsSecuritySettingsVisible);
        Assert.True(security.IsContentCreated);
        Assert.Same(model, security.Child!.DataContext);
        view.Measure(new Size(width, 540));
        view.Arrange(new Rect(0, 0, width, 540));
        Assert.True(view.DesiredSize.Width <= width);
        availability.SetResult(true);
        await opening;
    }

    [AvaloniaFact]
    public void NativeSecondaryHosts_AttachOnlyAfterTheirDeferredViewsAreCreated()
    {
        var view = CreateView();
        var groupPicker = new Border();
        var importReview = new Border();
        view.UseNativeGroupAccountPicker(groupPicker);
        view.UseNativeImportReviewList(importReview);
        var groupHost = view.FindControl<DeferredContent>("DeferredGroupEditor")!;
        var importHost = view.FindControl<DeferredContent>("DeferredImportReview")!;
        Assert.False(groupHost.IsContentCreated);
        Assert.False(importHost.IsContentCreated);

        groupHost.EnsureContentCreated();
        importHost.EnsureContentCreated();

        Assert.Contains(groupHost.GetLogicalDescendants(), control => ReferenceEquals(control, groupPicker));
        Assert.Contains(importHost.GetLogicalDescendants(), control => ReferenceEquals(control, importReview));
    }
}
