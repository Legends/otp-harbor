using Android.Content;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using TOTP.Avalonia.Mobile.Localization;
using TOTP.Avalonia.Mobile.Platform;
using TOTP.Avalonia.Mobile.Presentation;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Enums;
using TOTP.Core.Services.Interfaces;
using TOTP.Infrastructure.Extensions;
using TOTP.Infrastructure.Logging;
using TOTP.Infrastructure.Services;
using TOTP.Infrastructure.Storage;
using TOTP.Platform.Android;
using TOTP.Avalonia.Shared.Branding;
using TOTP.Avalonia.Shared.Diagnostics;
using TOTP.Core.Backup;
using AndroidBuild = global::Android.OS.Build;

namespace TOTP.Avalonia.Android;

internal static class AndroidCompositionRoot
{
    public static ServiceProvider Build(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var services = new ServiceCollection();
        var paths = new AndroidApplicationPaths(context);
        var fileSecurity = new AndroidFileSecurity(context);
        var configuration = new ConfigurationBuilder().Build();
        LoggingConfigurator.SetupEarlyLogger([], paths);
        VaultFileNameMigration.MigrateIfUsingDefaultPath(
            configuration,
            paths,
            fileSecurity);

        services.AddSingleton(context);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPlatformApplicationPaths>(paths);
        services.AddSingleton<ILogSwitchService>(
            new LogSwitchService(AppLogLevel.Information, isCliOverride: false));
        services.AddLogging(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddSerilog(Log.Logger, dispose: false)
            .AddProvider(new AndroidQuickUnlockLogProvider()));
        services.AddSingleton<AndroidActivityProvider>();
        services.AddSingleton<IAndroidBiometricPrompt, AndroidBiometricPrompt>();
        services.AddSingleton<IMobileQrScanner, AndroidQrScanner>();
        services.AddSingleton<IMobileQrImageFactory, MobileQrImageFactory>();
        services.AddSingleton<IMobileDocumentService, AndroidDocumentService>();
        services.AddSingleton<IDiagnosticLogEmailService, AndroidDiagnosticLogEmailService>();
        services.AddSingleton<IDiagnosticEnvironmentProvider>(new DiagnosticEnvironmentProvider(
            typeof(AndroidCompositionRoot).Assembly,
#if DEBUG
            "debug",
#else
            "direct",
#endif
            () => global::Avalonia.Application.Current?.ApplicationLifetime is
                global::Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime { MainView: { } view }
                    ? global::Avalonia.Controls.TopLevel.GetTopLevel(view) : null,
            () => new DiagnosticDevice(AndroidBuild.Manufacturer ?? string.Empty,
                AndroidBuild.Model ?? string.Empty,
                $"Android {AndroidBuild.VERSION.Release} (API {(int)AndroidBuild.VERSION.SdkInt})"),
            () =>
            {
                var package = context.PackageManager?.GetPackageInfo(context.PackageName!, 0);
                return package is null ? null : $"{package.VersionName} ({package.LongVersionCode})";
            },
            getDistribution: () =>
            {
#if DEBUG
                return "debug";
#else
                if (!OperatingSystem.IsAndroidVersionAtLeast(30)) return "direct";
                return context.PackageManager?.GetInstallSourceInfo(context.PackageName!)?.InstallingPackageName
                    == "com.android.vending" ? "play" : "direct";
#endif
            }));
        services.AddSingleton<AndroidPlatformUnattendedUnlock>();
        services.AddSingleton<IPlatformUnattendedUnlock>(provider =>
            provider.GetRequiredService<AndroidPlatformUnattendedUnlock>());
        services.AddSingleton<IPlatformQuickUnlock>(provider =>
            provider.GetRequiredService<AndroidPlatformUnattendedUnlock>());
        // Keep the interactive adapter last: single-service consumers are the
        // biometric enrollment/settings flow, while sessions enumerate both.
        services.AddSingleton<IPlatformQuickUnlock>(provider =>
            new AndroidPlatformQuickUnlock(
                provider.GetRequiredService<IAndroidBiometricPrompt>(),
                provider.GetRequiredService<ILogger<AndroidPlatformQuickUnlock>>(),
                AndroidQuickUnlockMode.DeviceCredential));
        // Register biometrics last so legacy single-service consumers keep the
        // secure default while the enrollment coordinator can enumerate both.
        services.AddSingleton<AndroidPlatformQuickUnlock>(provider =>
            new AndroidPlatformQuickUnlock(
                provider.GetRequiredService<IAndroidBiometricPrompt>(),
                provider.GetRequiredService<ILogger<AndroidPlatformQuickUnlock>>(),
                AndroidQuickUnlockMode.StrongBiometric));
        services.AddSingleton<IPlatformQuickUnlock>(provider =>
            provider.GetRequiredService<AndroidPlatformQuickUnlock>());
        services.AddInfrastructure(configuration, paths, fileSecurity);
        services.AddSingleton<IAutomaticBackupDestinationAdapter,
            AndroidAutomaticBackupDestinationAdapter>();
        services.AddSingleton<IAsyncPlatformClipboard, AndroidPlatformClipboard>();
        services.AddSingleton<AsyncClipboardService>();
        services.AddSingleton<IAsyncClipboardService>(provider =>
            provider.GetRequiredService<AsyncClipboardService>());
        services.AddSingleton<MobileStringCatalog>();
        services.AddSingleton<IBrandIconResolver, BrandIconResolver>();
        services.AddSingleton<MobileShellViewModel>();
        services.AddSingleton<IMobileLifecycleSink>(provider =>
            provider.GetRequiredService<MobileShellViewModel>());

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}
