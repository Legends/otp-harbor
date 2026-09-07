using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Platform;
using TOTP.Avalonia.Desktop.Startup;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Infrastructure.Security;
using TOTP.Infrastructure.Services;
using TOTP.Platform.Windows;
using TOTP.Platform.Windows.Security;
using AppLifetime = TOTP.Core.Services.Interfaces.IApplicationLifetime;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Camera.OpenCv;

namespace TOTP.Tests.Avalonia.Startup;

public sealed class AvaloniaCompositionRootTests
{
    [Fact]
    public void Build_RegistersAvaloniaPlatformContracts()
    {
        var desktopLifetimeMock = new Mock<IClassicDesktopStyleApplicationLifetime>();
        var desktopLifetime = desktopLifetimeMock.Object;
        var languageFlags = new Mock<ILanguageFlagProvider>().Object;

        using var services = AvaloniaCompositionRoot.Build(desktopLifetime, languageFlags);

        Assert.Same(
            desktopLifetime,
            services.GetRequiredService<IClassicDesktopStyleApplicationLifetime>());
        Assert.NotNull(services.GetRequiredService<IUiScheduler>());
        Assert.NotNull(services.GetRequiredService<AppLifetime>());
        Assert.IsType<WindowsApplicationPaths>(
            services.GetRequiredService<IPlatformApplicationPaths>());
        Assert.IsType<WindowsFileSecurity>(
            services.GetRequiredService<IPlatformFileSecurity>());
        Assert.NotNull(services.GetRequiredService<IConfiguration>());
        Assert.IsType<PortableSettingsService>(
            services.GetRequiredService<ISettingsService>());
        Assert.IsType<PortableAuthorizationService>(
            services.GetRequiredService<IAuthorizationService>());
        Assert.IsType<PlatformQuickUnlockEnrollment>(
            services.GetRequiredService<IPlatformQuickUnlockEnrollment>());
        Assert.IsType<WindowsPlatformQuickUnlock>(
            services.GetRequiredService<IPlatformQuickUnlock>());
        Assert.IsType<AvaloniaHelloPromptWindowHandleProvider>(
            services.GetRequiredService<IHelloPromptWindowHandleProvider>());
        Assert.IsType<HelloGate>(services.GetRequiredService<IHelloGate>());
        Assert.IsType<AccountManager>(
            services.GetRequiredService<IAccountManager>());
        Assert.IsType<AvaloniaStartupCoordinator>(
            services.GetRequiredService<IAvaloniaStartupCoordinator>());
        Assert.IsType<AvaloniaExceptionBoundary>(
            services.GetRequiredService<AvaloniaExceptionBoundary>());
        Assert.IsType<MainWindowViewModel>(
            services.GetRequiredService<MainWindowViewModel>());
        Assert.IsType<PasswordUnlockViewModel>(
            services.GetRequiredService<PasswordUnlockViewModel>());
        Assert.IsType<PasswordSetupViewModel>(
            services.GetRequiredService<PasswordSetupViewModel>());
        Assert.IsType<AccountListViewModel>(
            services.GetRequiredService<AccountListViewModel>());
        Assert.IsType<AccountQrCodeService>(
            services.GetRequiredService<IAccountQrCodeService>());
        Assert.IsType<QrAccountImportService>(
            services.GetRequiredService<IQrAccountImportService>());
        Assert.IsType<AvaloniaQrImageFactory>(
            services.GetRequiredService<IAvaloniaQrImageFactory>());
        Assert.IsType<AvaloniaFilePicker>(
            services.GetRequiredService<IAvaloniaFilePicker>());
        Assert.IsType<OpenCvQrImageDecoder>(
            services.GetRequiredService<IQrImageDecoder>());
        Assert.IsType<AvaloniaPlatformFolderLauncher>(
            services.GetRequiredService<IPlatformFolderLauncher>());
        Assert.IsType<AvaloniaDialogService>(
            services.GetRequiredService<IAvaloniaDialogService>());
        Assert.IsType<AvaloniaCameraScannerDialogService>(
            services.GetRequiredService<IAvaloniaCameraScannerDialogService>());
        Assert.IsType<AvaloniaQrPreviewDialogService>(
            services.GetRequiredService<IAvaloniaQrPreviewDialogService>());
        Assert.IsType<AvaloniaLocalizationService>(
            services.GetRequiredService<IAvaloniaLocalizationService>());
        Assert.Same(languageFlags, services.GetRequiredService<ILanguageFlagProvider>());
        Assert.IsType<NativeFilePickerViewModel>(
            services.GetRequiredService<NativeFilePickerViewModel>());
        Assert.IsType<NamedPipeActivationListener>(
            services.GetRequiredService<IActivationListener>());
        Assert.IsType<SettingsPageViewModel>(
            services.GetRequiredService<SettingsPageViewModel>());
        Assert.IsType<AuthorizationSettingsViewModel>(
            services.GetRequiredService<AuthorizationSettingsViewModel>());
        Assert.IsType<AsyncClipboardService>(
            services.GetRequiredService<IAsyncClipboardService>());
        Assert.Same(
            services.GetRequiredService<IdleMonitoringBackgroundService>(),
            services.GetRequiredService<IActivityHeartbeat>());
        Assert.NotNull(services.GetRequiredService<AvaloniaBackgroundServiceCoordinator>());
        desktopLifetimeMock.VerifySet(
            value => value.ShutdownMode = ShutdownMode.OnMainWindowClose,
            Times.Once);
    }
}
