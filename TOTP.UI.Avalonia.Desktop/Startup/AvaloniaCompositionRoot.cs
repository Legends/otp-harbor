using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using System.Globalization;
using Avalonia.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Platform;
using TOTP.Core.Services.Interfaces;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Infrastructure.Extensions;
using TOTP.Infrastructure.Security;
using TOTP.Infrastructure.Services;
using TOTP.Camera.OpenCv;
using TOTP.Avalonia.Desktop.Localization;
using Serilog;
using AppLifetime = TOTP.Core.Services.Interfaces.IApplicationLifetime;
#if TOTP_PLATFORM_WINDOWS
using TOTP.Platform.Windows;
using TOTP.Platform.Windows.Security;
#elif TOTP_PLATFORM_MACOS
using TOTP.Platform.MacOS;
using TOTP.Platform.MacOS.Security;
#elif TOTP_PLATFORM_LINUX
using TOTP.Platform.Linux;
using TOTP.Platform.Linux.Security;
#endif

namespace TOTP.Avalonia.Desktop.Startup;

public static class AvaloniaCompositionRoot
{
    public static ServiceProvider Build(
        IClassicDesktopStyleApplicationLifetime desktopLifetime,
        ILanguageFlagProvider? languageFlags = null)
    {
        ArgumentNullException.ThrowIfNull(desktopLifetime);
        desktopLifetime.ShutdownMode = ShutdownMode.OnMainWindowClose;

        var services = new ServiceCollection();
        var platformServices = DesktopPlatformServiceFactory.Create();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddEnvironmentVariables("TOTP_")
            .Build();

        services.AddSingleton(desktopLifetime);
        services.AddSingleton(Application.Current?.Resources ?? new ResourceDictionary());
        services.AddSingleton<AvaloniaStringCatalog>();
        services.AddSingleton<IAvaloniaLocalizationService, AvaloniaLocalizationService>();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IPlatformApplicationPaths>(platformServices.ApplicationPaths);
        services.AddLogging(builder => builder.AddSerilog(Log.Logger, dispose: false));
#if TOTP_PLATFORM_WINDOWS
        services.AddSingleton<IPlatformSessionEventSource, TOTP.Platform.Windows.WindowsSessionEventSource>();
        services.AddSingleton<IHelloPromptWindowHandleProvider, AvaloniaHelloPromptWindowHandleProvider>();
        services.AddSingleton<IHelloVerificationRequester, WindowsHelloVerificationRequester>();
        services.AddSingleton<IHelloGate, HelloGate>();
        services.AddSingleton<IPlatformQuickUnlock, WindowsPlatformQuickUnlock>();
#elif TOTP_PLATFORM_MACOS
        services.AddSingleton<IMacOSSessionStateReader, MacOSSessionStateReader>();
        services.AddSingleton<IPlatformSessionEventSource, MacOSSessionEventSource>();
        services.AddSingleton<ICameraAccessProbe, MacOSCameraAccessProbe>();
        services.AddSingleton<IMacOSKeychainNative, MacOSKeychainNative>();
        services.AddSingleton<MacOSKeychainSecretStore>();
        services.AddSingleton<IPlatformSecretStore>(provider =>
            provider.GetRequiredService<MacOSKeychainSecretStore>());
        services.AddSingleton<IPlatformQuickUnlock, MacOSPlatformQuickUnlock>();
#elif TOTP_PLATFORM_LINUX
        services.AddSingleton<ILinuxSessionMonitorRuntime, LinuxSessionMonitorRuntime>();
        services.AddSingleton<IPlatformSessionEventSource, LinuxSessionEventSource>();
        services.AddSingleton<ILinuxCameraDeviceAccess, LinuxCameraDeviceAccess>();
        services.AddSingleton<ICameraAccessProbe, LinuxCameraAccessProbe>();
        services.AddSingleton<ILinuxSecretServiceRuntime, LinuxSecretServiceRuntime>();
        services.AddSingleton<LinuxSecretServiceStore>();
        services.AddSingleton<IPlatformSecretStore>(provider =>
            provider.GetRequiredService<LinuxSecretServiceStore>());
        services.AddSingleton<IPlatformQuickUnlock, UnavailablePlatformQuickUnlock>();
#else
        services.AddSingleton<IPlatformSessionEventSource, UnavailablePlatformSessionEventSource>();
        services.AddSingleton<IPlatformQuickUnlock, UnavailablePlatformQuickUnlock>();
#endif
        services.AddInfrastructure(
            configuration,
            platformServices.ApplicationPaths,
            platformServices.FileSecurity);
#if TOTP_PLATFORM_WINDOWS
        services.AddSingleton<IWindowsUpdateInstallerRuntime, WindowsUpdateInstallerRuntime>();
        services.AddSingleton<IUpdateInstallerLauncher, WindowsUpdateInstallerLauncher>();
#endif
        services.AddSingleton(new AvaloniaUiScheduler(Dispatcher.UIThread));
        services.AddSingleton<IUiScheduler>(provider =>
            provider.GetRequiredService<AvaloniaUiScheduler>());
        services.AddSingleton<AppLifetime, AvaloniaApplicationLifetime>();
        services.AddSingleton<IActivationListener>(
            new NamedPipeActivationListener(DesktopInstanceIdentity.PipeName));
        services.AddSingleton<AvaloniaClipboardAccessor>();
        services.AddSingleton<AvaloniaWindowCoordinator>();
        if (languageFlags is null)
            services.AddSingleton<ILanguageFlagProvider, AvaloniaLanguageFlagProvider>();
        else
            services.AddSingleton(languageFlags);
        services.AddSingleton<IAvaloniaDialogService, AvaloniaDialogService>();
        services.AddSingleton<IAvaloniaCameraScannerDialogService,
            AvaloniaCameraScannerDialogService>();
        services.AddSingleton<IAvaloniaQrPreviewDialogService,
            AvaloniaQrPreviewDialogService>();
        services.AddSingleton<IAvaloniaFilePicker, AvaloniaFilePicker>();
        services.AddSingleton<IAvaloniaQrImageFactory, AvaloniaQrImageFactory>();
        services.AddSingleton<IPlatformFolderLauncher, AvaloniaPlatformFolderLauncher>();
        services.AddSingleton<IAsyncPlatformClipboard>(provider =>
            new AvaloniaPlatformClipboard(
                provider.GetRequiredService<AvaloniaClipboardAccessor>(),
                AvaloniaClipboardOwnershipPolicy.ForCurrentProcess(),
                provider.GetRequiredService<ILogger<AvaloniaPlatformClipboard>>()));
        services.AddSingleton<AsyncClipboardService>();
        services.AddSingleton<IAsyncClipboardService>(provider =>
            provider.GetRequiredService<AsyncClipboardService>());
        services.AddSingleton<IPlatformCapabilityReport, AvaloniaPlatformCapabilityReport>();
        services.AddSingleton<IAvaloniaStartupCoordinator, AvaloniaStartupCoordinator>();
        services.AddSingleton<AvaloniaExceptionBoundary>();
        services.AddSingleton<SessionLockPolicyBackgroundService>();
        services.AddSingleton<IdleMonitoringBackgroundService>();
        services.AddSingleton(provider => new AvaloniaBackgroundServiceCoordinator(
            [
                provider.GetRequiredService<SessionLockPolicyBackgroundService>(),
                provider.GetRequiredService<IdleMonitoringBackgroundService>()
            ],
            provider.GetRequiredService<ILogger<AvaloniaBackgroundServiceCoordinator>>()));
        services.AddSingleton<IActivityHeartbeat>(provider =>
            provider.GetRequiredService<IdleMonitoringBackgroundService>());
        services.AddSingleton<AvaloniaActivityMonitor>();
        services.AddSingleton<ICameraSessionFactory, OpenCvCameraSessionFactory>();
        services.AddSingleton<IQrScannerRunner, OpenCvQrScannerRunner>();
        services.AddSingleton<IQrImageDecoder, OpenCvQrImageDecoder>();
        services.AddSingleton<PasswordUnlockViewModel>();
        services.AddSingleton<PasswordSetupViewModel>();
        services.AddSingleton<AccountListViewModel>();
        services.AddSingleton<SettingsPageViewModel>();
        services.AddSingleton<AuthorizationSettingsViewModel>();
        services.AddSingleton<NativeFilePickerViewModel>();
        services.AddSingleton<CameraScannerViewModel>();
        services.AddSingleton<UpdateCheckViewModel>();
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient(provider => new MainWindow
        {
            DataContext = provider.GetRequiredService<MainWindowViewModel>(),
            ClipboardAccessor = provider.GetRequiredService<AvaloniaClipboardAccessor>(),
            WindowCoordinator = provider.GetRequiredService<AvaloniaWindowCoordinator>(),
            ActivityMonitor = provider.GetRequiredService<AvaloniaActivityMonitor>()
        });

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        provider.GetRequiredService<IAvaloniaLocalizationService>()
            .ApplyCulture(CultureInfo.CurrentUICulture.Name);
        return provider;
    }

}
